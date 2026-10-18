using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using GnollHackServer.Data.Legal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Legal;
using Overseer.Services.Privacy;
using Xunit;

namespace Overseer.Tests.Legal;

/// <summary>The GnollHack session-create endpoint: the app's acceptance, and the greeting held until the Terms are accepted.</summary>
public class SessionTermsHandoffTests : IDisposable
{
    private const string Secret = "test-antiforgery-token";
    private const string UserId = "terms-game-user";
    private const string UserName = "gnollhacker";

    private readonly TempWiki _wiki = new();
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "overseer-terms-session-" + Guid.NewGuid().ToString("N"));
    private readonly HeldScopeFactory _scopes = new();

    public void Dispose()
    {
        _scopes.Dispose();
        _wiki.Dispose();
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }

    private sealed class StubClaimsPrincipalFactory : IUserClaimsPrincipalFactory<ApplicationUser>
    {
        public Task<ClaimsPrincipal> CreateAsync(ApplicationUser user)
            => Task.FromResult(LegalTestSupport.Principal(user.Id));
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

    private IConfiguration Config()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "AntiForgeryToken", Secret },
            { "ConversationsDataLocation", _dataDirectory },
            { "PrivacySettings:KeyRing:v1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) },
            { "PrivacySettings:ActiveKeyVersion", "v1" },
            { "ChatRetentionSettings:MaxActiveSessionsPerUser", "50" },
            { "ChatRetentionSettings:SoftDeleteGracePeriodDays", "30" }
        }).Build();

    private sealed record Fixture(
        ApplicationDbContext Db,
        SessionController Controller,
        OngoingChatManager Ongoing,
        TermsAcceptanceStore Terms,
        PendingGreetingStore Greetings);

    private Fixture Create()
    {
        _wiki.Write(LegalTestSupport.RealFirstLines);
        var config = Config();
        var db = LegalTestSupport.Db();
        var userStore = new DictionaryUserStore();
        userStore.Users[UserId] = new ApplicationUser { Id = UserId, UserName = UserName, EmailConfirmed = true };
        var signIn = new StubSignInManager(new DictionaryUserManager(userStore));
        var ongoing = new OngoingChatManager(config);
        var controller = new SessionController(
            signIn, db, new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }), config,
            _scopes, ongoing, new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance),
            new ConfidentialPolicyResolver(config),
            new ContentProtectionService(new ConfigurationContentKeyRing(config)),
            NullLogger<SessionController>.Instance);
        var reader = LegalTestSupport.Reader(_wiki.Path);
        return new Fixture(db, controller, ongoing, LegalTestSupport.Store(db, reader), new PendingGreetingStore(LegalTestSupport.Cache()));
    }

    private static CreateSessionRequest Request(string? acceptedTermsVersion = null, string? initialPrompt = "Hello, Overseer") => new()
    {
        UserName = UserName,
        Password = "correct-password",
        AntiForgeryToken = Secret,
        SnapshotText = "Dlvl 3",
        IsGnollHackSession = true,
        InitialPrompt = initialPrompt,
        AcceptedTermsVersion = acceptedTermsVersion,
        OverseerSettings = "{\"StringData\":{\"GHVersion\":\"4.2.0\",\"PortVersion\":\"2.1.0\"}}"
    };

    private static long SessionIdOf(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return (long)ok.Value!.GetType().GetProperty("sessionId")!.GetValue(ok.Value)!;
    }

    [Fact]
    public async Task WithoutAcceptanceTheGreetingIsHeld()
    {
        var f = Create();
        using var db = f.Db;

        long sessionId = SessionIdOf(await f.Controller.Create(Request(), terms: f.Terms, pendingGreetings: f.Greetings));

        Assert.Null(f.Ongoing.TryGet(SessionRef.Persistent(sessionId)));
        Assert.Empty(db.UserTermsAcceptances);
        Assert.Equal("Hello, Overseer", f.Greetings.TryTake(sessionId, UserId));
    }

    [Fact]
    public async Task AMatchingAppVersionWritesAGnollHackAppRowAndStartsTheTurn()
    {
        var f = Create();
        using var db = f.Db;

        long sessionId = SessionIdOf(await f.Controller.Create(Request("2026-10-18"), terms: f.Terms, pendingGreetings: f.Greetings));

        var row = Assert.Single(db.UserTermsAcceptances);
        Assert.Equal(LegalDocumentKeys.SourceGnollHackApp, row.Source);
        Assert.Equal("2026-10-18", row.Version);
        Assert.Equal("4.2.0 2.1.0", row.ClientVersion);
        Assert.Null(row.ContentSha256);
        Assert.NotNull(f.Ongoing.TryGet(SessionRef.Persistent(sessionId)));
        Assert.Null(f.Greetings.TryTake(sessionId, UserId));
    }

    [Fact]
    public async Task AnOlderAppVersionWritesNothingAndHoldsTheGreeting()
    {
        var f = Create();
        using var db = f.Db;

        long sessionId = SessionIdOf(await f.Controller.Create(Request("2026-10-01"), terms: f.Terms, pendingGreetings: f.Greetings));

        Assert.Empty(db.UserTermsAcceptances);
        Assert.Null(f.Ongoing.TryGet(SessionRef.Persistent(sessionId)));
        Assert.Equal("Hello, Overseer", f.Greetings.TryTake(sessionId, UserId));
    }

    [Fact]
    public async Task AnEarlierWebAcceptanceStartsTheTurnWithoutAnAppVersion()
    {
        var f = Create();
        using var db = f.Db;
        await LegalTestSupport.AddAcceptanceAsync(db, UserId, "2026-10-18");

        long sessionId = SessionIdOf(await f.Controller.Create(Request(), terms: f.Terms, pendingGreetings: f.Greetings));

        Assert.NotNull(f.Ongoing.TryGet(SessionRef.Persistent(sessionId)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2026-10-18")]
    [InlineData("not-a-version")]
    [InlineData("")]
    public async Task CreateNeverFailsBecauseOfTheTerms(string? acceptedTermsVersion)
    {
        var f = Create();
        using var db = f.Db;

        var result = await f.Controller.Create(Request(acceptedTermsVersion, initialPrompt: null), terms: f.Terms, pendingGreetings: f.Greetings);

        Assert.IsType<OkObjectResult>(result);
        Assert.Single(db.ChatSession);
    }

    [Fact]
    public async Task ABrokenStoreStillCreatesTheSessionAndHoldsTheGreeting()
    {
        var f = Create();
        using var db = f.Db;
        // A store whose database is already disposed throws on every call.
        var brokenDb = LegalTestSupport.Db();
        var broken = LegalTestSupport.Store(brokenDb, LegalTestSupport.Reader(null));
        await brokenDb.DisposeAsync();

        long sessionId = SessionIdOf(await f.Controller.Create(Request("2026-10-18"), terms: broken, pendingGreetings: f.Greetings));

        Assert.Equal("Hello, Overseer", f.Greetings.TryTake(sessionId, UserId));
    }

    [Fact]
    public void TheClientVersionIsReadFromTheAppsSettings()
    {
        Assert.Equal("4.2.0 2.1.0", SessionController.ReadClientVersion("{\"StringData\":{\"GHVersion\":\"4.2.0\",\"PortVersion\":\"2.1.0\"}}"));
        Assert.Equal("4.2.0", SessionController.ReadClientVersion("{\"StringData\":{\"GHVersion\":\"4.2.0\"}}"));
        Assert.Null(SessionController.ReadClientVersion(null));
        Assert.Null(SessionController.ReadClientVersion("not json"));
        Assert.Equal(64, SessionController.ReadClientVersion("{\"StringData\":{\"GHVersion\":\"" + new string('9', 100) + "\"}}")!.Length);
    }
}
