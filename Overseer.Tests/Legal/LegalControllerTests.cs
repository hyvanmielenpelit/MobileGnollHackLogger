using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GnollHackServer.Data.Legal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Legal;
using Overseer.Services.Privacy;
using Xunit;

namespace Overseer.Tests.Legal;

/// <summary>The Terms document, version and accept endpoints, and the operator statements.</summary>
public class LegalControllerTests : IDisposable
{
    private const string Owner = "legal-owner";
    private const string Other = "legal-other";
    private readonly TempWiki _wiki = new();

    public void Dispose() => _wiki.Dispose();

    private static object? Read(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value);

    private static LegalController Controller(LegalDocumentReader reader, TermsAcceptanceStore store, string? userId = null, IConfiguration? configuration = null)
        => new(reader, store, configuration ?? LegalTestSupport.Configuration(null))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = LegalTestSupport.Principal(userId) }
            }
        };

    [Fact]
    public async Task TheDocumentIsServedAnonymously()
    {
        _wiki.Write(LegalTestSupport.RealFirstLines);
        using var db = LegalTestSupport.Db();
        var reader = LegalTestSupport.Reader(_wiki.Path);
        var controller = Controller(reader, LegalTestSupport.Store(db, reader));

        var ok = Assert.IsType<OkObjectResult>(await controller.GetTerms(LegalDocumentKeys.OverseerTerms, TestContext.Current.CancellationToken));

        Assert.Equal("2026-10-18", Read(ok.Value!, "version"));
        Assert.Equal("18 October 2026", Read(ok.Value!, "versionLabel"));
        Assert.Equal(LegalTestSupport.TermsPage, Read(ok.Value!, "title"));
        Assert.StartsWith("**Last updated:**", (string)Read(ok.Value!, "markdown")!);
        Assert.Equal(64, ((string)Read(ok.Value!, "contentSha256")!).Length);
        Assert.Equal("https://wiki.gnollhack.com/Gnoll%20Overseer%20Terms%20of%20Service.md", Read(ok.Value!, "wikiUrl"));
        Assert.Equal("https://wiki.gnollhack.com/Gnoll%20Overseer%20Privacy%20Policy.md", Read(ok.Value!, "privacyPolicyUrl"));
    }

    [Fact]
    public async Task AnUnreadableFileServesTheFallbackVersionAndNoText()
    {
        using var db = LegalTestSupport.Db();
        await LegalTestSupport.AddAcceptanceAsync(db, Other, "2026-10-18.2");
        var reader = LegalTestSupport.Reader(_wiki.Path);
        var controller = Controller(reader, LegalTestSupport.Store(db, reader));

        var ok = Assert.IsType<OkObjectResult>(await controller.GetTerms(LegalDocumentKeys.OverseerTerms, TestContext.Current.CancellationToken));

        Assert.Null(Read(ok.Value!, "markdown"));
        Assert.Equal("2026-10-18.2", Read(ok.Value!, "version"));
        Assert.Equal("18 October 2026 (revision 2)", Read(ok.Value!, "versionLabel"));
        Assert.Equal("https://wiki.gnollhack.com/Gnoll%20Overseer%20Terms%20of%20Service.md", Read(ok.Value!, "wikiUrl"));
    }

    [Fact]
    public async Task TheVersionEndpointAnswersAnonymously()
    {
        _wiki.Write("**Last updated:** 18 October 2026 (revision 2)\n");
        using var db = LegalTestSupport.Db();
        var reader = LegalTestSupport.Reader(_wiki.Path);
        var controller = Controller(reader, LegalTestSupport.Store(db, reader));

        var ok = Assert.IsType<OkObjectResult>(await controller.GetTermsVersion(LegalDocumentKeys.OverseerTerms, TestContext.Current.CancellationToken));
        Assert.Equal("2026-10-18.2", Read(ok.Value!, "version"));

        var method = typeof(LegalController).GetMethod(nameof(LegalController.GetTermsVersion))!;
        Assert.NotNull(method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), false).SingleOrDefault());
    }

    [Fact]
    public async Task AnUnknownKeyIsNotFound()
    {
        using var db = LegalTestSupport.Db();
        var reader = LegalTestSupport.Reader(_wiki.Path);
        var controller = Controller(reader, LegalTestSupport.Store(db, reader), Owner);
        var ct = TestContext.Current.CancellationToken;

        Assert.IsType<NotFoundResult>(await controller.GetTerms("account-terms", ct));
        Assert.IsType<NotFoundResult>(await controller.GetTermsVersion("nonexistent", ct));
        Assert.IsType<NotFoundResult>(await controller.AcceptTerms("nonexistent", new LegalController.AcceptTermsRequest { Version = "2026-10-18" }, ct: ct));
    }

    [Fact]
    public async Task AcceptReturns409WhenTheVersionIsStale()
    {
        _wiki.Write("**Last updated:** 18 October 2026 (revision 2)\n");
        using var db = LegalTestSupport.Db();
        var reader = LegalTestSupport.Reader(_wiki.Path);
        var controller = Controller(reader, LegalTestSupport.Store(db, reader), Owner);

        var result = await controller.AcceptTerms(LegalDocumentKeys.OverseerTerms,
            new LegalController.AcceptTermsRequest { Version = "2026-10-18" }, ct: TestContext.Current.CancellationToken);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal("terms_version_changed", Read(conflict.Value!, "error"));
        Assert.Equal("2026-10-18.2", Read(conflict.Value!, "requiredVersion"));
        Assert.Empty(db.UserTermsAcceptances);
    }

    [Fact]
    public async Task AcceptRecordsAWebRow()
    {
        _wiki.Write(LegalTestSupport.RealFirstLines);
        using var db = LegalTestSupport.Db();
        var reader = LegalTestSupport.Reader(_wiki.Path);
        var controller = Controller(reader, LegalTestSupport.Store(db, reader), Owner);
        string hash = reader.Get(LegalDocumentKeys.OverseerTerms)!.ContentSha256;

        var ok = Assert.IsType<OkObjectResult>(await controller.AcceptTerms(LegalDocumentKeys.OverseerTerms,
            new LegalController.AcceptTermsRequest { Version = "2026-10-18", ContentSha256 = hash }, ct: TestContext.Current.CancellationToken));

        Assert.Equal(true, Read(ok.Value!, "accepted"));
        Assert.Equal(false, Read(ok.Value!, "greetingStarted"));
        var row = Assert.Single(db.UserTermsAcceptances);
        Assert.Equal(LegalDocumentKeys.SourceWeb, row.Source);
        Assert.Equal(hash, row.ContentSha256);
    }

    [Fact]
    public async Task AHeldGreetingStartsOnceAndOnlyForItsOwner()
    {
        var ct = TestContext.Current.CancellationToken;
        _wiki.Write(LegalTestSupport.RealFirstLines);
        using var db = LegalTestSupport.Db();
        var session = new ChatSession { AspNetUserId = Owner, Title = "GnollHack Session", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(ct);

        var reader = LegalTestSupport.Reader(_wiki.Path);
        var config = LegalTestSupport.Configuration(null);
        var ongoing = new OngoingChatManager(config);
        using var scopes = new HeldScopeFactory();
        var starter = new HandoffGreetingStarter(db, config, scopes, ongoing);
        var greetings = new PendingGreetingStore(LegalTestSupport.Cache());
        var userStore = new DictionaryUserStore();
        userStore.Users[Owner] = new ApplicationUser { Id = Owner, UserName = "owner" };
        userStore.Users[Other] = new ApplicationUser { Id = Other, UserName = "other" };
        var users = new DictionaryUserManager(userStore);

        greetings.Hold(session.Id, Owner, "Hello, Overseer");
        var request = new LegalController.AcceptTermsRequest { Version = "2026-10-18", SessionId = session.Id };

        // Another user naming the session starts nothing, and leaves the greeting held.
        var otherResult = Assert.IsType<OkObjectResult>(await Controller(reader, LegalTestSupport.Store(db, reader), Other)
            .AcceptTerms(LegalDocumentKeys.OverseerTerms, request, greetings, starter, users, db, ct));
        Assert.Equal(false, Read(otherResult.Value!, "greetingStarted"));
        Assert.Null(ongoing.TryGet(SessionRef.Persistent(session.Id)));

        var ownerResult = Assert.IsType<OkObjectResult>(await Controller(reader, LegalTestSupport.Store(db, reader), Owner)
            .AcceptTerms(LegalDocumentKeys.OverseerTerms, request, greetings, starter, users, db, ct));
        Assert.Equal(true, Read(ownerResult.Value!, "greetingStarted"));
        Assert.NotNull(ongoing.TryGet(SessionRef.Persistent(session.Id)));

        var again = Assert.IsType<OkObjectResult>(await Controller(reader, LegalTestSupport.Store(db, reader), Owner)
            .AcceptTerms(LegalDocumentKeys.OverseerTerms, request, greetings, starter, users, db, ct));
        Assert.Equal(false, Read(again.Value!, "greetingStarted"));
    }

    [Fact]
    public void WithNothingConfiguredOperatorReturnsEmptyStatements()
    {
        using var db = LegalTestSupport.Db();
        var reader = LegalTestSupport.Reader(_wiki.Path);
        var controller = Controller(reader, LegalTestSupport.Store(db, reader));

        var ok = Assert.IsType<OkObjectResult>(controller.GetOperatorStatements());

        var statements = Assert.IsAssignableFrom<IDictionary<string, LegalController.OperatorStatement>>(Read(ok.Value!, "statements"));
        Assert.Empty(statements);
    }

    [Fact]
    public void ConfiguredOperatorStatementsAreServedWithTheirSources()
    {
        using var db = LegalTestSupport.Db();
        var reader = LegalTestSupport.Reader(_wiki.Path);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "PrivacyNotice:OperatorStatements:contact:Text", "Write to us." },
            { "PrivacyNotice:OperatorStatements:contact:Source", "Privacy Policy" },
            { "PrivacyNotice:OperatorStatements:contact:SourceUrl", "https://wiki.gnollhack.com/x.md" },
            { "PrivacyNotice:OperatorStatements:breachNotification:Text", "Assessed by the operator." },
            { "PrivacyNotice:OperatorStatements:breachNotification:Source", "Operator statement, 2026-10-18" },
            { "PrivacyNotice:OperatorStatements:legalEntity:Text", "  " }
        }).Build();
        var controller = Controller(reader, LegalTestSupport.Store(db, reader), configuration: config);

        var ok = Assert.IsType<OkObjectResult>(controller.GetOperatorStatements());
        var statements = Assert.IsAssignableFrom<IDictionary<string, LegalController.OperatorStatement>>(Read(ok.Value!, "statements"));

        Assert.Equal(2, statements.Count);
        Assert.Equal(new LegalController.OperatorStatement("Write to us.", "Privacy Policy", "https://wiki.gnollhack.com/x.md"), statements["contact"]);
        Assert.Null(statements["breachNotification"].SourceUrl);
        Assert.False(statements.ContainsKey("legalEntity"));
    }

    [Fact]
    public void TheRepositoryConfigurationSuppliesAllSevenOperatorStatements()
    {
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        Assert.SkipUnless(System.IO.File.Exists(path), "appsettings.json is not copied to the test output.");
        var config = new ConfigurationBuilder().AddJsonFile(path).Build();
        using var db = LegalTestSupport.Db();
        var reader = LegalTestSupport.Reader(_wiki.Path);

        var ok = Assert.IsType<OkObjectResult>(Controller(reader, LegalTestSupport.Store(db, reader), configuration: config).GetOperatorStatements());
        var statements = Assert.IsAssignableFrom<IDictionary<string, LegalController.OperatorStatement>>(Read(ok.Value!, "statements"));

        Assert.Equal(
            new[] { "acceptableUse", "breachNotification", "contact", "dataProcessingAgreement", "dataResidency", "legalEntity", "telemetryRetention" },
            statements.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.All(statements.Values, s => Assert.False(string.IsNullOrWhiteSpace(s.Source)));
    }
}
