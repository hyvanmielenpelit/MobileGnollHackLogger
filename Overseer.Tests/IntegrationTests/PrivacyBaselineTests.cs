using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.IntegrationTests;

/// <summary>
/// Stage A baseline protections that only show up through the real pipeline: the response
/// headers, the login endpoint's shape, and the snapshot flags the game-client upload path
/// writes.
/// </summary>
public class PrivacyBaselineTests : IClassFixture<OverseerWebApplicationFactory>
{
    private readonly OverseerWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PrivacyBaselineTests(OverseerWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8];

    private async Task<ApplicationUser> CreateUserAsync(string password, bool twoFactor = false)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = Unique("id-"),
            UserName = Unique("privacy-user-"),
            Email = Unique("privacy-") + "@example.com",
            EmailConfirmed = true
        };

        var created = await userManager.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));

        if (twoFactor)
        {
            /* TwoFactorEnabled alone is not enough: SignInOrTwoFactorAsync consults
               GetValidTwoFactorProvidersAsync, and the authenticator provider only reports
               itself usable once a key exists. */
            await userManager.ResetAuthenticatorKeyAsync(user);
            await userManager.SetTwoFactorEnabledAsync(user, true);
        }

        return user;
    }

    [Fact]
    public async Task SecurityHeaders_ArePresentOnAnApiResponse()
    {
        var response = await _client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);

        Assert.True(response.Headers.TryGetValues("Content-Security-Policy", out var csp));
        Assert.Contains("default-src 'self'", csp!.Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task SecurityHeaders_ArePresentOnTheSpaFallbackPath()
    {
        /* index.html is an Angular build output and is absent in a test run, so this is a 404.
           The headers are asserted anyway: the middleware sits before UseStaticFiles precisely
           so that the SPA route is covered whatever it resolves to. */
        var response = await _client.GetAsync("/chat", TestContext.Current.CancellationToken);

        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Login_UnknownUserAndWrongPassword_AreByteIdenticalAndCarryNoSignInResult()
    {
        var user = await CreateUserAsync("Correct-Horse-Battery-1");

        var unknown = await _client.PostAsJsonWithoutCharsetAsync(
            "/api/auth/login", new { userName = Unique("nobody-"), password = "Correct-Horse-Battery-1" });
        var wrongPassword = await _client.PostAsJsonWithoutCharsetAsync(
            "/api/auth/login", new { userName = user.UserName, password = "Wrong-Horse-Battery-9" });

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);

        string unknownBody = await unknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        string wrongBody = await wrongPassword.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Byte-identical: nothing about the account may vary with the response.
        Assert.Equal(unknownBody, wrongBody);

        /* And in particular no serialised SignInResult, which would disclose IsLockedOut and
           RequiresTwoFactor to a caller who has not proved the password. */
        foreach (var body in new[] { unknownBody, wrongBody })
        {
            Assert.DoesNotContain("isLockedOut", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("requiresTwoFactor", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("isNotAllowed", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("succeeded", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Login_TwoFactorEnabledUser_ReturnsADistinctTwoFactorOutcomeInsteadOfFailing()
    {
        /* The F-6 regression: this used to fall through to Unauthorized, so enabling TOTP in
           the account app locked the user out of Overseer entirely. */
        var user = await CreateUserAsync("Correct-Horse-Battery-2", twoFactor: true);

        var response = await _client.PostAsJsonWithoutCharsetAsync(
            "/api/auth/login", new { userName = user.UserName, password = "Correct-Horse-Battery-2" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"requiresTwoFactor\":true", body);
        Assert.Contains("\"hasAuthenticator\":true", body);

        // Not a completed sign-in: no identity is published.
        Assert.DoesNotContain("\"userName\"", body);
    }

    [Fact]
    public async Task LoginTwoFactor_WithoutAPriorPasswordStep_IsRefused()
    {
        var response = await _client.PostAsJsonWithoutCharsetAsync(
            "/api/auth/login/2fa", new { code = "123456", rememberMachine = false });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SessionCreate_SetsIsGameSnapshotAndIsMessageHistoryOnTheMessagesItWrites()
    {
        /* SessionController is the game-client writer, and it was the one v3 named while
           omitting ChatController.AttachSnapshot. Both are asserted -- this one here, the
           other in ChatAttachSnapshotTests. */
        var user = await CreateUserAsync("Correct-Horse-Battery-3");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["AntiForgeryToken"] = "test-antiforgery-secret",
            ["UserName"] = user.UserName!,
            ["Password"] = "Correct-Horse-Battery-3",
            ["Title"] = "Flag test session",
            ["SnapshotHtml"] = "<pre>Dungeon Level 3</pre>",
            ["MessageHistory"] = "You hit the gnoll. The gnoll bites!",
            ["IsGnollHackSession"] = "true"
        });

        var response = await _client.PostAsync("/api/session/create", form, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var session = await db.ChatSession
            .Where(s => s.AspNetUserId == user.Id)
            .OrderByDescending(s => s.Id)
            .FirstAsync(TestContext.Current.CancellationToken);

        var messages = await db.ChatMessage
            .Where(m => m.ChatSessionId == session.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(messages, m => m.IsGameSnapshot);
        Assert.Contains("Dungeon Level 3", snapshot.Content);

        var history = Assert.Single(messages, m => m.IsMessageHistory);
        Assert.Contains("bites", history.Content);

        // The two are distinct rows, not one row wearing both flags.
        Assert.NotEqual(snapshot.Id, history.Id);
    }
}

internal static class PrivacyBaselineHttpExtensions
{
    /// <summary>
    /// Posts JSON without a charset parameter on the content type.
    /// </summary>
    /// <remarks>
    /// <c>PostAsJsonAsync</c> sends <c>application/json; charset=utf-8</c>, which this API's
    /// model binding accepts, but spelling the request out keeps the login assertions above
    /// comparing bodies rather than negotiated framing.
    /// </remarks>
    public static Task<HttpResponseMessage> PostAsJsonWithoutCharsetAsync(
        this HttpClient client, string url, object payload)
    {
        var content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(payload),
            System.Text.Encoding.UTF8,
            "application/json");
        return client.PostAsync(url, content, TestContext.Current.CancellationToken);
    }
}
