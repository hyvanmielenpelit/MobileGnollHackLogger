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
/// Password reset, email confirmation, email change and account deletion through a browser session: what each
/// answers to an unknown, unconfirmed or wrong input, who receives email, and what changes in the database.
/// </summary>
public partial class AccountRecoveryTests : IClassFixture<AccountRecoveryTests.RecoveryFactory>
{
    private const string ForgotPasswordPath = "/Identity/Account/ForgotPassword";
    private const string ResetPasswordPath = "/Identity/Account/ResetPassword";
    private const string ResendConfirmationPath = "/Identity/Account/ResendEmailConfirmation";
    private const string ConfirmEmailChangePath = "/Identity/Account/ConfirmEmailChange";
    private const string NewPassword = "New-Passw0rd-1";
    private const byte IpBlock = 0x22;

    private readonly RecoveryFactory _factory;

    public AccountRecoveryTests(RecoveryFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// The website with room for many account page requests per address, and the <c>LogFile</c> setting the
    /// ConfirmEmail and Register page models require.
    /// </summary>
    public class RecoveryFactory : WebsiteWebApplicationFactory
    {
        protected override IDictionary<string, string?> Settings
        {
            get
            {
                var settings = base.Settings;
                settings["GameApiSecurity:AccountPagePermitsPerMinute"] = "1000";
                settings["LogFile"] = Path.Combine(TempRoot, "website.log");
                return settings;
            }
        }
    }

    [Fact]
    public async Task ForgotPassword_AnswersAlikeAndEmailsOnlyTheConfirmedAddress()
    {
        var confirmed = await CreateUserAsync("fp_confirmed");
        var unconfirmed = await CreateUserAsync("fp_unconfirmed", u => u.EmailConfirmed = false);
        string unknownEmail = WebsiteTestData.UniqueUserName("fp_unknown") + "@example.invalid";
        using var session = NewSession();
        int before = _factory.Emails.Sent.Count;

        var responses = new List<HttpResponseMessage>();
        foreach (string email in new[] { unknownEmail, unconfirmed.Email!, confirmed.Email! })
        {
            responses.Add(await PostForgotPasswordAsync(session, email));
        }

        Assert.All(responses, r =>
        {
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
            Assert.Equal("/Identity/Account/ForgotPasswordConfirmation", r.Headers.Location?.OriginalString);
        });
        var sent = Assert.Single(_factory.Emails.Sent.Skip(before));
        Assert.Equal(confirmed.Email, sent.Email);
    }

    [Fact]
    public async Task ResetPassword_CodeWorksOnlyForItsOwnUserAndOnlyOnce()
    {
        var userA = await CreateUserAsync("rp_a");
        var userB = await CreateUserAsync("rp_b");
        using var session = NewSession();
        int before = _factory.Emails.Sent.Count;
        Assert.Equal(HttpStatusCode.Redirect, (await PostForgotPasswordAsync(session, userA.Email!)).StatusCode);
        var sent = Assert.Single(_factory.Emails.Sent.Skip(before));

        string link = BrowserSession.LinkIn(sent.HtmlMessage, ResetPasswordPath);
        string urlCode = QueryHelpers.ParseQuery(new Uri(link).Query)["code"].ToString();
        string resetPage = ResetPasswordPath + "?code=" + Uri.EscapeDataString(urlCode);
        var page = await session.GetAsync(resetPage);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        string code = InputValue(await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), "Input.Code");
        Assert.Equal(Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(urlCode)), code);

        var crossUser = await PostResetPasswordAsync(session, resetPage, userB.Email!, code);

        Assert.Equal(HttpStatusCode.OK, crossUser.StatusCode);
        Assert.NotEmpty(SummaryMessages(await crossUser.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
        using (var userBSession = NewSession())
        {
            await userBSession.SignInAsync(userB.UserName!, WebsiteTestData.Password);
        }

        var firstUse = await PostResetPasswordAsync(session, resetPage, userA.Email!, code);

        Assert.Equal(HttpStatusCode.Redirect, firstUse.StatusCode);
        Assert.Equal("/Identity/Account/ResetPasswordConfirmation", firstUse.Headers.Location?.OriginalString);

        // The reset changed the security stamp the code was issued against.
        var secondUse = await PostResetPasswordAsync(session, resetPage, userA.Email!, code);

        Assert.Equal(HttpStatusCode.OK, secondUse.StatusCode);
        Assert.NotEmpty(SummaryMessages(await secondUse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task ConfirmEmail_WrongCode_LeavesEmailUnconfirmed()
    {
        var user = await CreateUserAsync("ce_wrong", u => u.EmailConfirmed = false);
        using var session = NewSession();
        // Valid Base64Url, so the page decodes it and Identity rejects the token.
        string wrongCode = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("not-a-confirmation-token"));

        var response = await session.GetAsync("/Identity/Account/ConfirmEmail?userId=" + Uri.EscapeDataString(user.Id)
            + "&code=" + Uri.EscapeDataString(wrongCode));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Error confirming your email.", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.False((await FindUserByIdAsync(user.Id)).EmailConfirmed);
    }

    [Fact]
    public async Task ResendEmailConfirmation_UnknownAndKnown_AnswerAlikeAndEmailOnlyTheKnownAddress()
    {
        var unconfirmed = await CreateUserAsync("rc_known", u => u.EmailConfirmed = false);
        string unknownEmail = WebsiteTestData.UniqueUserName("rc_unknown") + "@example.invalid";
        using var session = NewSession();
        int before = _factory.Emails.Sent.Count;

        foreach (string email in new[] { unknownEmail, unconfirmed.Email! })
        {
            var response = await session.PostFormAsync(ResendConfirmationPath, new Dictionary<string, string>
            {
                ["Input.Email"] = email
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("Verification email sent. Please check your email.", body);
            Assert.DoesNotContain("name=\"Input.Email\"", body);
        }

        var sent = Assert.Single(_factory.Emails.Sent.Skip(before));
        Assert.Equal(unconfirmed.Email, sent.Email);
    }

    [Fact]
    public async Task ChangeEmail_EmailsNewAddressAndChangesNothingUntilConfirmed()
    {
        var userA = await CreateUserAsync("chg_a");
        var userB = await CreateUserAsync("chg_b");
        string newEmail = WebsiteTestData.UniqueUserName("chg_new") + "@example.invalid";
        using var session = NewSession();
        await session.SignInAsync(userA.UserName!, WebsiteTestData.Password);
        int before = _factory.Emails.Sent.Count;

        var change = await session.PostFormAsync("/Identity/Account/Manage/Email?handler=ChangeEmail",
            new Dictionary<string, string> { ["Input.NewEmail"] = newEmail }, tokenFromPath: "/Identity/Account/Manage/Email");

        Assert.Equal(HttpStatusCode.Redirect, change.StatusCode);
        var sent = Assert.Single(_factory.Emails.Sent.Skip(before));
        Assert.Equal(newEmail, sent.Email);
        Assert.Equal(userA.Email, (await FindUserByIdAsync(userA.Id)).Email);

        var query = QueryHelpers.ParseQuery(new Uri(BrowserSession.LinkIn(sent.HtmlMessage, ConfirmEmailChangePath)).Query);
        Assert.Equal(userA.Id, query["userId"].ToString());
        string code = query["code"].ToString();

        var crossUser = await session.GetAsync(ConfirmEmailChangePath + "?userId=" + Uri.EscapeDataString(userB.Id)
            + "&email=" + Uri.EscapeDataString(newEmail) + "&code=" + Uri.EscapeDataString(code));

        Assert.Equal(HttpStatusCode.OK, crossUser.StatusCode);
        Assert.Contains("Error changing email.", await crossUser.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var storedA = await FindUserByIdAsync(userA.Id);
        var storedB = await FindUserByIdAsync(userB.Id);
        Assert.Equal(userA.Email, storedA.Email);
        Assert.Equal(userA.UserName, storedA.UserName);
        Assert.Equal(userB.Email, storedB.Email);
        Assert.Equal(userB.UserName, storedB.UserName);

        var confirm = await session.GetAsync(ConfirmEmailChangePath + "?userId=" + Uri.EscapeDataString(userA.Id)
            + "&email=" + Uri.EscapeDataString(newEmail) + "&code=" + Uri.EscapeDataString(code));

        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal(newEmail, (await FindUserByIdAsync(userA.Id)).Email);
    }

    [Fact]
    public async Task DeletePersonalData_WrongPassword_KeepsTheAccount()
    {
        var user = await CreateUserAsync("del_wrong");
        using var session = NewSession();
        await session.SignInAsync(user.UserName!, WebsiteTestData.Password);

        var response = await session.PostFormAsync("/Identity/Account/Manage/DeletePersonalData",
            new Dictionary<string, string> { ["Input.Password"] = "Wrong-Passw0rd" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Incorrect password.",
            SummaryMessages(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
        var stored = await _factory.WithScopeAsync(sp =>
            sp.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(user.Id));
        Assert.NotNull(stored);
    }

    private BrowserSession NewSession() => new(_factory, BrowserSession.NextIp(IpBlock));

    private Task<ApplicationUser> CreateUserAsync(string prefix, Action<ApplicationUser>? configure = null)
        => _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, WebsiteTestData.UniqueUserName(prefix), configure));

    private static Task<HttpResponseMessage> PostForgotPasswordAsync(BrowserSession session, string email) =>
        session.PostFormAsync(ForgotPasswordPath, new Dictionary<string, string> { ["Input.Email"] = email });

    /// <summary>Posts the ResetPassword form; the antiforgery token comes from <paramref name="resetPage"/>, which carries the code.</summary>
    private static Task<HttpResponseMessage> PostResetPasswordAsync(BrowserSession session, string resetPage, string email, string code) =>
        session.PostFormAsync(ResetPasswordPath, new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = NewPassword,
            ["Input.ConfirmPassword"] = NewPassword,
            ["Input.Code"] = code
        }, tokenFromPath: resetPage);

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

    /// <summary>The items of the rendered validation summary.</summary>
    private static List<string> SummaryMessages(string html)
    {
        var summary = ValidationSummaryRegex().Match(html);
        return summary.Success
            ? ListItemRegex().Matches(summary.Groups[1].Value).Select(m => WebUtility.HtmlDecode(m.Groups[1].Value).Trim()).ToList()
            : [];
    }

    [GeneratedRegex(@"<input\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InputTagRegex();

    [GeneratedRegex(@"\bvalue\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex ValueAttributeRegex();

    [GeneratedRegex(@"<div\b[^>]*\bvalidation-summary-errors\b[^>]*>\s*<ul>(.*?)</ul>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ValidationSummaryRegex();

    [GeneratedRegex(@"<li\b[^>]*>(.*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ListItemRegex();
}
