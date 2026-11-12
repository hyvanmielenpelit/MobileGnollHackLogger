using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Pages;

/// <summary>
/// The Identity pages an emailed link opens never render a page under a URL that carries an account ID, a code or an
/// email address: each redirects to its own path, and the redirected request renders what TempData kept.
/// </summary>
public partial class IdentityUrlPrivacyTests : IClassFixture<AccountRecoveryTests.RecoveryFactory>
{
    private const string ConfirmEmailPath = "/Identity/Account/ConfirmEmail";
    private const string ConfirmEmailChangePath = "/Identity/Account/ConfirmEmailChange";
    private const string ResetPasswordPath = "/Identity/Account/ResetPassword";
    private const string RegisterConfirmationPath = "/Identity/Account/RegisterConfirmation";
    private const string TempDataCookieName = ".AspNetCore.Mvc.CookieTempDataProvider";
    private const byte IpBlock = 0x24;

    private readonly AccountRecoveryTests.RecoveryFactory _factory;

    public IdentityUrlPrivacyTests(AccountRecoveryTests.RecoveryFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Every combination answers a redirect to the page's own path with no query, except ResetPassword with a code that
    /// is not Base64Url, which is the plain-text 400.
    /// </summary>
    [Theory]
    [InlineData(ConfirmEmailPath, "valid")]
    [InlineData(ConfirmEmailPath, "wrong")]
    [InlineData(ConfirmEmailPath, "malformed")]
    [InlineData(ConfirmEmailChangePath, "valid")]
    [InlineData(ConfirmEmailChangePath, "wrong")]
    [InlineData(ConfirmEmailChangePath, "malformed")]
    [InlineData(ResetPasswordPath, "valid")]
    [InlineData(ResetPasswordPath, "wrong")]
    [InlineData(ResetPasswordPath, "malformed")]
    [InlineData(RegisterConfirmationPath, "none")]
    public async Task EmailedLinkPage_NeverRendersHtmlUnderItsQueryUrl(string path, string codeKind)
    {
        var user = await CreateUserAsync("url_guard", u => u.EmailConfirmed = path != ConfirmEmailPath);
        string newEmail = WebsiteTestData.UniqueUserName("url_new") + "@example.invalid";
        string code = codeKind switch
        {
            "valid" => await ValidCodeAsync(path, user.Id, newEmail),
            "wrong" => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("not-a-valid-token")),
            "malformed" => "%25%25",
            _ => ""
        };
        string escapedCode = codeKind == "malformed" ? code : Uri.EscapeDataString(code);
        string url = path switch
        {
            ConfirmEmailPath => path + "?userId=" + Uri.EscapeDataString(user.Id) + "&code=" + escapedCode,
            ConfirmEmailChangePath => path + "?userId=" + Uri.EscapeDataString(user.Id) + "&email=" + Uri.EscapeDataString(newEmail)
                + "&code=" + escapedCode,
            ResetPasswordPath => path + "?code=" + escapedCode,
            _ => path + "?email=" + Uri.EscapeDataString(user.Email!) + "&returnUrl=%2F"
        };
        using var session = NewSession();

        var response = await session.GetAsync(url);

        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        if (path == ResetPasswordPath && codeKind == "malformed")
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
            return;
        }
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(path, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ConfirmEmail_ValidCode_ConfirmsAndShowsResultOnCleanUrl()
    {
        var user = await CreateUserAsync("ce_valid", u => u.EmailConfirmed = false);
        string code = await ValidCodeAsync(ConfirmEmailPath, user.Id, null);
        using var session = NewSession();

        var redirect = await session.GetAsync(ConfirmEmailPath + "?userId=" + Uri.EscapeDataString(user.Id)
            + "&code=" + Uri.EscapeDataString(code));

        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal(ConfirmEmailPath, redirect.Headers.Location?.OriginalString);
        var page = await session.GetAsync(ConfirmEmailPath);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Thank you for confirming your email.", await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True((await FindUserByIdAsync(user.Id)).EmailConfirmed);

        // The result is read once, so a reload goes home.
        var reload = await session.GetAsync(ConfirmEmailPath);
        Assert.Equal(HttpStatusCode.Redirect, reload.StatusCode);
        Assert.Equal("/", reload.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ConfirmEmail_CleanUrlWithoutResult_RedirectsHome()
    {
        using var session = NewSession();

        var response = await session.GetAsync(ConfirmEmailPath);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        // Reading an empty TempData answers a deletion of the cookie (an empty value); a stored value is what must not appear.
        var storedTempData = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Where(c => c.StartsWith(TempDataCookieName + "=", StringComparison.Ordinal)
                && !c.StartsWith(TempDataCookieName + "=;", StringComparison.Ordinal)).ToList()
            : [];
        Assert.True(storedTempData.Count == 0, "The response stores the TempData cookie: " + string.Join(" | ", storedTempData));
    }

    [Fact]
    public async Task ResetPassword_CleanUrl_ReloadKeepsCode()
    {
        var user = await CreateUserAsync("rp_reload");
        string urlCode = await ValidCodeAsync(ResetPasswordPath, user.Id, null);
        using var session = NewSession();

        var redirect = await session.GetAsync(ResetPasswordPath + "?code=" + Uri.EscapeDataString(urlCode));
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal(ResetPasswordPath, redirect.Headers.Location?.OriginalString);

        string expected = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(urlCode));
        for (int i = 0; i < 2; i++)
        {
            var page = await session.GetAsync(ResetPasswordPath);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Equal(expected, InputValue(await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), "Input.Code"));
        }
    }

    [Fact]
    public async Task ResetPassword_CleanUrlWithoutCode_IsBadRequest()
    {
        using var session = NewSession();

        var response = await session.GetAsync(ResetPasswordPath);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterConfirmation_LegacyEmailQuery_RedirectsToCleanUrl()
    {
        using var session = NewSession();

        var redirect = await session.GetAsync(RegisterConfirmationPath + "?email=" + Uri.EscapeDataString("legacy@example.invalid")
            + "&returnUrl=%2F");

        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal(RegisterConfirmationPath, redirect.Headers.Location?.OriginalString);
        for (int i = 0; i < 2; i++)
        {
            var page = await session.GetAsync(RegisterConfirmationPath);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("Check Your Email", await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task RegisterConfirmation_WithoutRegistration_RedirectsHome()
    {
        using var session = NewSession();

        var response = await session.GetAsync(RegisterConfirmationPath);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Register_RedirectsToRegisterConfirmationWithoutQuery()
    {
        string userName = WebsiteTestData.UniqueUserName("reg_clean");
        using var session = NewSession();

        var response = await session.PostFormAsync("/Identity/Account/Register", new Dictionary<string, string>
        {
            ["Input.UserName"] = userName,
            ["Input.Email"] = userName + "@example.invalid",
            ["Input.Password"] = WebsiteTestData.Password,
            ["Input.ConfirmPassword"] = WebsiteTestData.Password
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(RegisterConfirmationPath, response.Headers.Location?.OriginalString);
        var page = await session.GetAsync(RegisterConfirmationPath);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
    }

    private BrowserSession NewSession() => new(_factory, BrowserSession.NextIp(IpBlock));

    private Task<ApplicationUser> CreateUserAsync(string prefix, Action<ApplicationUser>? configure = null)
        => _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, WebsiteTestData.UniqueUserName(prefix), configure));

    /// <summary>A Base64Url-encoded token of the kind the page at <paramref name="path"/> accepts, as its emailed link carries it.</summary>
    private Task<string> ValidCodeAsync(string path, string userId, string? newEmail)
        => _factory.WithScopeAsync(async sp =>
        {
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId) ?? throw new InvalidOperationException("No user " + userId + ".");
            string token = path switch
            {
                ConfirmEmailPath => await userManager.GenerateEmailConfirmationTokenAsync(user),
                ConfirmEmailChangePath => await userManager.GenerateChangeEmailTokenAsync(user, newEmail!),
                _ => await userManager.GeneratePasswordResetTokenAsync(user)
            };
            return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        });

    /// <summary>The user as stored, read through a fresh context.</summary>
    private async Task<ApplicationUser> FindUserByIdAsync(string userId)
    {
        var user = await _factory.WithScopeAsync(sp =>
            sp.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId));
        Assert.NotNull(user);
        return user;
    }

    /// <summary>The HTML-decoded value of the input named <paramref name="name"/>.</summary>
    private static string InputValue(string html, string name)
    {
        foreach (Match input in InputTagRegex().Matches(html))
        {
            if (!input.Value.Contains("name=\"" + name + "\"", StringComparison.Ordinal))
            {
                continue;
            }
            var value = ValueAttributeRegex().Match(input.Value);
            if (value.Success)
            {
                return WebUtility.HtmlDecode(value.Groups[1].Value);
            }
        }
        throw new InvalidOperationException("No input named " + name + " with a value.");
    }

    [GeneratedRegex(@"<input\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InputTagRegex();

    [GeneratedRegex(@"\bvalue\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex ValueAttributeRegex();
}
