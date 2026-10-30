using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Pages;

/// <summary>
/// A signed-in browser session: its cookies, sign-out, the security stamp that ends it, the two-factor step, and the
/// return URL of the Terms acceptance page.
/// </summary>
public class AccountSessionTests : IClassFixture<AccountSessionTests.SessionFactory>, IClassFixture<AccountSessionTests.TermsFactory>
{
    private const string LoginPath = BrowserSession.LoginPath;
    private const string ManagePath = "/Identity/Account/Manage";
    private const string AcceptTermsPath = "/Identity/Account/AcceptTerms";
    private const string TokenPagePath = "/Identity/Account/Manage/Email";
    private const string TwoFactorPath = "/Identity/Account/LoginWith2fa";
    private const string RecoveryCodePath = "/Identity/Account/LoginWithRecoveryCode";
    private const byte IpBlock = 0x21;

    public static TheoryData<string> OffSiteReturnUrls => new()
    {
        "https://evil.example.test/",
        "//evil.example.test/",
        "/\\evil.example.test/",
    };

    private readonly SessionFactory _factory;
    private readonly TermsFactory _termsFactory;

    public AccountSessionTests(SessionFactory factory, TermsFactory termsFactory)
    {
        _factory = factory;
        _termsFactory = termsFactory;
    }

    /// <summary>The website validating the security stamp on every request, with room for many account page requests per address.</summary>
    public class SessionFactory : WebsiteWebApplicationFactory
    {
        protected override IDictionary<string, string?> Settings
        {
            get
            {
                var settings = base.Settings;
                settings["IdentityLockout:SecurityStampValidationMinutes"] = "0";
                settings["GameApiSecurity:AccountPagePermitsPerMinute"] = "1000";
                return settings;
            }
        }
    }

    /// <summary>The website with an account Terms file in its wiki folder, so a signed-in user without an acceptance is sent to accept it.</summary>
    public class TermsFactory : WebsiteWebApplicationFactory
    {
        /// <summary>The <c>WikiPageName</c> of <c>LegalDocuments:Documents:account-terms</c> in appsettings.json.</summary>
        private const string TermsFileName = "GnollHack Account Terms of Service.md";

        public TermsFactory()
        {
            File.WriteAllText(Path.Combine(WikiPath, TermsFileName),
                "**Last updated:** 18 October 2026\n"
                + "\n"
                + "These Terms of Service govern your GnollHack account.\n");
        }
    }

    [Fact]
    public async Task SignIn_CookiesCarrySecurityFlags()
    {
        string userName = await CreateUserAsync("session_cookie");
        using var session = NewSession(_factory);

        var loginPage = await session.GetAsync(LoginPath);
        var signIn = await PostLoginAsync(session, userName);

        string antiforgery = Assert.Single(SetCookies(loginPage), c => c.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal));
        Assert.Contains("httponly", antiforgery, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        string application = Assert.Single(SetCookies(signIn), c => c.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal));
        Assert.Contains("secure", application, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", application, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", application, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        string userName = await CreateUserAsync("session_logout");
        using var session = NewSession(_factory);
        await session.SignInAsync(userName, WebsiteTestData.Password);

        var logout = await session.PostFormAsync("/Identity/Account/Logout", new Dictionary<string, string>(), tokenFromPath: TokenPagePath);

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        AssertRedirectsToLogin(await session.GetAsync(ManagePath));
    }

    [Fact]
    public async Task UpdateSecurityStamp_NextRequestIsAnonymous()
    {
        string userName = await CreateUserAsync("session_stamp");
        using var session = NewSession(_factory);
        await session.SignInAsync(userName, WebsiteTestData.Password);
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync(ManagePath)).StatusCode);

        await _factory.WithScopeAsync(async sp =>
        {
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByNameAsync(userName);
            Assert.NotNull(user);
            Assert.True((await userManager.UpdateSecurityStampAsync(user)).Succeeded);
        });

        AssertRedirectsToLogin(await session.GetAsync(ManagePath));
    }

    [Fact]
    public async Task Ban_EndsTheSessionAndRefusesTheNextSignIn()
    {
        string userName = await CreateUserAsync("session_ban");
        using var session = NewSession(_factory);
        await session.SignInAsync(userName, WebsiteTestData.Password);
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync(ManagePath)).StatusCode);

        // As Overseer's admin service bans: the flag, then a new security stamp, which saves the user.
        await _factory.WithScopeAsync(async sp =>
        {
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByNameAsync(userName);
            Assert.NotNull(user);
            user.IsBanned = true;
            Assert.True((await userManager.UpdateSecurityStampAsync(user)).Succeeded);
        });

        AssertRedirectsToLogin(await session.GetAsync(ManagePath));
        var signIn = await PostLoginAsync(session, userName);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.Contains("This account has been banned.",
            await signIn.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TwoFactorAccount_PasswordStepDoesNotSignIn()
    {
        string userName = await CreateTwoFactorUserAsync("session_2fa");
        using var session = NewSession(_factory);

        var response = await PostLoginAsync(session, userName);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Identity/Account/LoginWith2fa", PathOf(response.Headers.Location));
        AssertRedirectsToLogin(await session.GetAsync(ManagePath));
    }

    [Theory]
    [InlineData(TwoFactorPath)]
    [InlineData(RecoveryCodePath)]
    public async Task TwoFactorPages_NoTwoFactorCookie_GetRedirectsToLoginKeepingReturnUrl(string path)
    {
        using var session = NewSession(_factory);

        var response = await session.GetAsync(path + "?returnUrl=%2FTopScores");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(LoginPath + "?ReturnUrl=%2FTopScores", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData(TwoFactorPath, "Input.TwoFactorCode", "123456")]
    [InlineData(RecoveryCodePath, "Input.RecoveryCode", "ABCDE-12345")]
    public async Task TwoFactorPages_NoTwoFactorCookie_PostRedirectsToLogin(string path, string field, string value)
    {
        using var session = NewSession(_factory);

        var response = await session.PostFormAsync(path, new Dictionary<string, string> { [field] = value }, tokenFromPath: LoginPath);

        AssertRedirectsToLogin(response);
    }

    [Fact]
    public async Task TwoFactorAccount_AfterPasswordStep_TwoFactorPagesOpen()
    {
        string userName = await CreateTwoFactorUserAsync("session_2fa_pages");
        using var session = NewSession(_factory);
        await PostLoginAsync(session, userName);

        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync(TwoFactorPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync(RecoveryCodePath)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(OffSiteReturnUrls))]
    public async Task AcceptTerms_NoTermsFile_OffSiteReturnUrlRedirectsHome(string returnUrl)
    {
        string userName = await CreateUserAsync("session_terms_off");
        using var session = NewSession(_factory);
        await session.SignInAsync(userName, WebsiteTestData.Password);

        var post = await session.PostFormAsync(AcceptTermsPath, new Dictionary<string, string> { ["ReturnUrl"] = returnUrl },
            tokenFromPath: TokenPagePath);
        var get = await session.GetAsync(AcceptTermsPath + "?ReturnUrl=" + Uri.EscapeDataString(returnUrl));

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Equal("/", post.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, get.StatusCode);
        Assert.Equal("/", get.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AcceptTerms_NoTermsFile_LocalReturnUrlIsFollowed()
    {
        string userName = await CreateUserAsync("session_terms_local");
        using var session = NewSession(_factory);
        await session.SignInAsync(userName, WebsiteTestData.Password);

        var post = await session.PostFormAsync(AcceptTermsPath, new Dictionary<string, string> { ["ReturnUrl"] = "/TopScores" },
            tokenFromPath: TokenPagePath);
        var get = await session.GetAsync(AcceptTermsPath + "?ReturnUrl=" + Uri.EscapeDataString("/TopScores"));

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Equal("/TopScores", post.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, get.StatusCode);
        Assert.Equal("/TopScores", get.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task TermsFilter_SignedInNotAccepted_ReturnUrlKeepsPathAndQuery()
    {
        string userName = WebsiteTestData.UniqueUserName("session_terms_filter");
        await _termsFactory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        using var session = NewSession(_termsFactory);
        await session.SignInAsync(userName, WebsiteTestData.Password);

        var response = await session.GetAsync("/TopScores?mode=normal");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(AcceptTermsPath + "?returnUrl=%2FTopScores%3Fmode%3Dnormal", response.Headers.Location?.OriginalString);
    }

    private static BrowserSession NewSession(WebsiteWebApplicationFactory factory) => new(factory, BrowserSession.NextIp(IpBlock));

    private async Task<string> CreateUserAsync(string prefix)
    {
        string userName = WebsiteTestData.UniqueUserName(prefix);
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        return userName;
    }

    private async Task<string> CreateTwoFactorUserAsync(string prefix)
    {
        string userName = WebsiteTestData.UniqueUserName(prefix);
        await _factory.WithScopeAsync(async sp =>
        {
            var user = await WebsiteTestData.CreateUserAsync(sp, userName);
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await userManager.ResetAuthenticatorKeyAsync(user)).Succeeded);
            Assert.True((await userManager.SetTwoFactorEnabledAsync(user, true)).Succeeded);
        });
        return userName;
    }

    private static Task<HttpResponseMessage> PostLoginAsync(BrowserSession session, string userName) =>
        session.PostFormAsync(LoginPath, new Dictionary<string, string>
        {
            ["Input.UserName"] = userName,
            ["Input.Password"] = WebsiteTestData.Password
        });

    private static IEnumerable<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values : Enumerable.Empty<string>();

    /// <summary>The cookie handler's challenge: a redirect to the Login page, whose Location is absolute.</summary>
    private static void AssertRedirectsToLogin(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(LoginPath, PathOf(response.Headers.Location));
    }

    private static string PathOf(Uri? location)
    {
        Assert.NotNull(location);
        return new Uri(new Uri("https://localhost"), location).AbsolutePath;
    }
}
