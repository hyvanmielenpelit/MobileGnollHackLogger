using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using GnollHackServer.Data.Legal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Pages;

public class AccountTermsFilterTests : IClassFixture<AccountTermsFilterTests.TestAuthFactory>
{
    public const string UserIdHeader = "X-Test-User-Id";

    /// <summary>The <c>WikiPageName</c> of <c>LegalDocuments:Documents:account-terms</c> in appsettings.json.</summary>
    private const string TermsFileName = "GnollHack Account Terms of Service.md";

    private const string TermsText =
        "**Last updated:** 18 October 2026\n"
        + "\n"
        + "These Terms of Service govern your GnollHack account.\n";

    private readonly TestAuthFactory _factory;

    public AccountTermsFilterTests(TestAuthFactory factory)
    {
        _factory = factory;

        string path = Path.Combine(_factory.WikiPath, TermsFileName);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, TermsText);
        }
    }

    [Fact]
    public async Task SignedInNotAccepted_GetIndex_RedirectsToAcceptTerms()
    {
        var user = await CreateUserAsync("terms_no");
        using var client = _factory.CreateGameClient();

        var response = await GetAsUserAsync(client, "/", user);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Identity/Account/AcceptTerms?returnUrl=%2F", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task SignedInAccepted_GetIndex_Returns200()
    {
        var user = await CreateUserAsync("terms_yes");
        await AcceptTermsAsync(user);
        using var client = _factory.CreateGameClient();

        var response = await GetAsUserAsync(client, "/", user);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_GetIndex_Returns200()
    {
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SignedInNotAccepted_GetAcceptTerms_Returns200()
    {
        var user = await CreateUserAsync("terms_page");
        using var client = _factory.CreateGameClient();

        var response = await GetAsUserAsync(client, "/Identity/Account/AcceptTerms", user);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SignedInNotAccepted_GetXLogFile_Returns200()
    {
        var user = await CreateUserAsync("terms_api");
        using var client = _factory.CreateGameClient();

        var response = await GetAsUserAsync(client, "/xlogfile", user);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/?list=recent")]
    [InlineData("/?list=top")]
    [InlineData("/?list=top&mode=normal&death=ascended")]
    public async Task SignedInAccepted_Dashboard_Returns200(string url)
    {
        var user = await CreateUserAsync("dash");
        var other = await CreateUserAsync("dash_other");
        await _factory.WithScopeAsync(async sp =>
        {
            long start = 1_761_000_000;
            await WebsiteTestData.AddGameLogAsync(sp, user,
                WebsiteTestData.XLogEntry(user.UserName!, 800_000, start, mode: "normal", turns: 42_000, death: "ascended"));
            await WebsiteTestData.AddGameLogAsync(sp, user,
                WebsiteTestData.XLogEntry(user.UserName!, 1200, start + 1000, mode: "modern"));
            await WebsiteTestData.AddGameLogAsync(sp, other,
                WebsiteTestData.XLogEntry(other.UserName!, 950_000, start + 2000, mode: "normal", turns: 50_000, death: "ascended"));

            var bones = await WebsiteTestData.AddBonesAsync(sp, other, difficulty: 0, version: 1, compatVersion: 1);
            await WebsiteTestData.AddDownloadTransactionAsync(sp, user, bones);
        });
        await AcceptTermsAsync(user);
        using var client = _factory.CreateGameClient();

        var response = await GetAsUserAsync(client, url, user);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("| Dashboard", body);
    }

    private Task<ApplicationUser> CreateUserAsync(string prefix)
        => _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, WebsiteTestData.UniqueUserName(prefix)));

    /// <summary>Records an acceptance of the version the Terms file on disk requires now.</summary>
    private Task AcceptTermsAsync(ApplicationUser user)
        => _factory.WithScopeAsync(async sp =>
        {
            var store = sp.GetRequiredService<TermsAcceptanceStore>();
            string version = await store.GetRequiredVersionAsync(LegalDocumentKeys.AccountTerms, TestContext.Current.CancellationToken)
                ?? throw new InvalidOperationException("The account Terms have no required version; the Terms file was not read.");
            var result = await store.RecordAsync(user.Id, LegalDocumentKeys.AccountTerms, version, LegalDocumentKeys.SourceWeb,
                null, null, TestContext.Current.CancellationToken);
            Assert.Equal(TermsRecordResult.Recorded, result);
        });

    private static async Task<HttpResponseMessage> GetAsUserAsync(HttpClient client, string url, ApplicationUser user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(UserIdHeader, user.Id);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>The website host with a test authentication scheme that signs in the user named by <see cref="UserIdHeader"/>.</summary>
    public class TestAuthFactory : WebsiteWebApplicationFactory
    {
        public const string SchemeName = "Test";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(o =>
                    {
                        o.DefaultAuthenticateScheme = SchemeName;
                        o.DefaultScheme = SchemeName;
                        o.DefaultChallengeScheme = SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestUserIdAuthHandler>(SchemeName, _ => { });
            });
        }
    }

    /// <summary>
    /// Signs in the existing user whose Id is in <see cref="UserIdHeader"/>; no header is an anonymous request.
    /// The identity carries Identity's application scheme as its authentication type, so
    /// <c>SignInManager.IsSignedIn</c> treats it as a signed-in user, as the layout's login partial checks.
    /// </summary>
    public class TestUserIdAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestUserIdAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Context.Request.Headers.TryGetValue(UserIdHeader, out var values) || string.IsNullOrEmpty(values.ToString()))
            {
                return AuthenticateResult.NoResult();
            }

            string userId = values.ToString();
            var userManager = Context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return AuthenticateResult.Fail("No user has the Id " + userId + ".");
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName ?? "")
            };
            var identity = new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme);
            var principal = new ClaimsPrincipal(identity);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
        }
    }
}
