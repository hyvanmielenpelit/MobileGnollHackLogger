using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Areas.API;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Security;

/// <summary>
/// Every page and game API POST action the host maps, against pinned lists: which pages an anonymous visitor
/// reaches, which pages demand a token on POST, and which actions are flood-limited. A new endpoint fails these
/// tests until it is placed on a list.
/// </summary>
public class EndpointInventoryTests : IClassFixture<EndpointInventoryTests.InventoryFactory>,
    IClassFixture<EndpointInventoryTests.PostFloodFactory>
{
    private const string LoginChallengePrefix = "/Identity/Account/Login?ReturnUrl=";

    /// <summary>Pages an anonymous GET reaches: anything but the authorization challenge.</summary>
    private static readonly string[] AnonymousPages =
    [
        "/",
        "/BonesTransactions",
        "/Contact",
        "/Error",
        "/Privacy",
        "/RecentGames",
        "/Statistics",
        "/Test",
        "/TestBones",
        "/TestSFTCreate",
        "/TestSFTUse",
        "/TopScores",
        "/Identity/Error",
        "/Identity/Account/AccessDenied",
        "/Identity/Account/ConfirmEmail",
        "/Identity/Account/ConfirmEmailChange",
        "/Identity/Account/ExternalLogin",
        "/Identity/Account/ForgotPassword",
        "/Identity/Account/ForgotPasswordConfirmation",
        "/Identity/Account/Lockout",
        "/Identity/Account/Login",
        "/Identity/Account/LoginWith2fa",
        "/Identity/Account/LoginWithRecoveryCode",
        "/Identity/Account/Register",
        "/Identity/Account/RegisterConfirmation",
        "/Identity/Account/ResendEmailConfirmation",
        "/Identity/Account/ResetPassword",
        "/Identity/Account/ResetPasswordConfirmation",
    ];

    /// <summary>
    /// Pages that send an anonymous GET to the Login page: <c>AcceptTerms</c> through its <c>[Authorize]</c>,
    /// <c>Logout</c> and <c>Manage</c> through Identity UI's conventions.
    /// </summary>
    private static readonly string[] ProtectedPages =
    [
        "/Identity/Account/AcceptTerms",
        "/Identity/Account/Logout",
        "/Identity/Account/Manage",
        "/Identity/Account/Manage/ChangePassword",
        "/Identity/Account/Manage/DeletePersonalData",
        "/Identity/Account/Manage/Disable2fa",
        "/Identity/Account/Manage/DownloadPersonalData",
        "/Identity/Account/Manage/Email",
        "/Identity/Account/Manage/EnableAuthenticator",
        "/Identity/Account/Manage/ExternalLogins",
        "/Identity/Account/Manage/GenerateRecoveryCodes",
        "/Identity/Account/Manage/JunetHack",
        "/Identity/Account/Manage/PersonalData",
        "/Identity/Account/Manage/ResetAuthenticator",
        "/Identity/Account/Manage/SetPassword",
        "/Identity/Account/Manage/ShowRecoveryCodes",
        "/Identity/Account/Manage/TwoFactorAuthentication",
    ];

    /// <summary>The only page that accepts a POST without an antiforgery token.</summary>
    private const string IgnoresAntiforgeryPage = "/Error";

    /// <summary>Route templates of the game API POST actions, compared case-insensitively.</summary>
    private static readonly string[] GameApiPostRoutes =
    [
        "xlogfile",
        "bones",
        "api/SaveFileTracking/create",
        "api/SaveFileTracking/use",
        "api/replay",
    ];

    private readonly InventoryFactory _factory;
    private readonly PostFloodFactory _floodFactory;

    public EndpointInventoryTests(InventoryFactory factory, PostFloodFactory floodFactory)
    {
        _factory = factory;
        _floodFactory = floodFactory;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The website with the account-page rate limit out of reach of a walk over every page.</summary>
    public class InventoryFactory : WebsiteWebApplicationFactory
    {
        protected override IDictionary<string, string?> Settings
        {
            get
            {
                var settings = base.Settings;
                settings["GameApiSecurity:AccountPagePermitsPerMinute"] = "100000";
                return settings;
            }
        }
    }

    /// <summary>The website with two game API POSTs per address a minute.</summary>
    public class PostFloodFactory : WebsiteWebApplicationFactory
    {
        protected override IDictionary<string, string?> Settings
        {
            get
            {
                var settings = base.Settings;
                settings["GameApiSecurity:FloodRequestsPerMinute"] = "2";
                return settings;
            }
        }
    }

    [Fact]
    public async Task Pages_AnonymousGet_OnlyTheAllowlistIsReachable()
    {
        var reachable = new List<string>();
        var protectedPages = new List<string>();

        foreach (string path in PageEndpoints(_factory.Services).Keys)
        {
            using var session = new BrowserSession(_factory, BrowserSession.NextIp(0x10));

            var response = await session.GetAsync(path);

            (IsLoginChallenge(response) ? protectedPages : reachable).Add(path);
        }

        Assert.Equal(Sorted(AnonymousPages), Sorted(reachable));
        Assert.Equal(Sorted(ProtectedPages), Sorted(protectedPages));
    }

    [Fact]
    public void Pages_EnumeratedSet_MatchesThePinnedList()
    {
        var discovered = PageEndpoints(_factory.Services).Keys;

        Assert.Equal(Sorted(AnonymousPages.Concat(ProtectedPages)), Sorted(discovered));
    }

    [Fact]
    public async Task Pages_SignedInPostWithoutToken_Returns400()
    {
        string userName = WebsiteTestData.UniqueUserName("inventory");
        await _factory.WithScopeAsync(sp => WebsiteTestData.CreateUserAsync(sp, userName));
        using var session = new BrowserSession(_factory, BrowserSession.NextIp(0x11));
        await session.SignInAsync(userName, WebsiteTestData.Password);

        var notRejected = new List<string>();
        foreach (string path in PageEndpoints(_factory.Services).Keys)
        {
            if (path == IgnoresAntiforgeryPage)
            {
                continue;
            }

            var response = await session.PostFormAsync(path, new Dictionary<string, string>(), includeToken: false);

            if (response.StatusCode != HttpStatusCode.BadRequest)
            {
                notRejected.Add(path + " answered " + (int)response.StatusCode);
            }
        }

        Assert.Empty(notRejected);

        // The tokenless POST to Logout left the session signed in.
        var manage = await session.GetAsync("/Identity/Account/Manage");
        Assert.Equal(HttpStatusCode.OK, manage.StatusCode);
    }

    [Fact]
    public async Task Pages_OnlyErrorIgnoresAntiforgery()
    {
        var loader = _factory.Services.GetRequiredService<PageLoader>();
        var ignoring = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (path, endpoint) in PageEndpoints(_factory.Services))
        {
            var descriptor = endpoint.Metadata.GetMetadata<PageActionDescriptor>()!;
            CompiledPageActionDescriptor compiled = await loader.LoadAsync(descriptor, endpoint.Metadata);

            // The attribute instance reaches both the endpoint metadata and the filters; it is counted once.
            int count = compiled.EndpointMetadata.OfType<IgnoreAntiforgeryTokenAttribute>()
                .Concat(compiled.FilterDescriptors.Select(f => f.Filter).OfType<IgnoreAntiforgeryTokenAttribute>())
                .Distinct<object>(ReferenceEqualityComparer.Instance)
                .Count();
            if (count > 0)
            {
                ignoring[path] = count;
            }
        }

        var only = Assert.Single(ignoring);
        Assert.Equal(IgnoresAntiforgeryPage, only.Key);
        Assert.Equal(1, only.Value);
    }

    [Fact]
    public async Task GameApiPosts_EveryPostAction_IsFloodLimited()
    {
        var postEndpoints = _floodFactory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() != null
                && e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Post, StringComparer.OrdinalIgnoreCase) == true)
            .ToList();

        Assert.Equal(Sorted(GameApiPostRoutes.Select(r => r.ToLowerInvariant())),
            Sorted(postEndpoints.Select(e => (e.RoutePattern.RawText ?? "").ToLowerInvariant())));

        using var client = _floodFactory.CreateGameClient();
        foreach (var endpoint in postEndpoints)
        {
            string route = endpoint.RoutePattern.RawText!;
            Assert.Equal(GameApiRateLimits.GameApiFlood, endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);

            string ip = BrowserSession.NextIp(0x12);
            for (int i = 0; i < 2; i++)
            {
                var allowed = await PostEmptyFormAsync(client, route, ip);
                Assert.True(allowed.StatusCode == HttpStatusCode.BadRequest,
                    route + " answered " + (int)allowed.StatusCode + " to request " + (i + 1) + "; expected 400.");
            }

            var rejected = await PostEmptyFormAsync(client, route, ip);

            Assert.True(rejected.StatusCode == (HttpStatusCode)429,
                route + " answered " + (int)rejected.StatusCode + " to request 3; expected 429.");
        }
    }

    [Fact]
    public async Task DevelopmentOnlyEndpoints_AreNotMapped()
    {
        using var client = _factory.CreateGameClient();

        var response = await client.GetAsync("/dev/sentry-test", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The page endpoints the host maps at startup, one per page under its path: an <c>Index</c> route and its
    /// folder route are one page. These endpoints carry the route and the uncompiled descriptor, not the page
    /// model's attributes.
    /// </summary>
    private static SortedDictionary<string, RouteEndpoint> PageEndpoints(IServiceProvider services)
    {
        var pages = new SortedDictionary<string, RouteEndpoint>(StringComparer.Ordinal);
        foreach (var endpoint in services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            if (endpoint.Metadata.GetMetadata<PageActionDescriptor>() == null)
            {
                continue;
            }
            pages.TryAdd(PagePath(endpoint.RoutePattern.RawText), endpoint);
        }
        return pages;
    }

    /// <summary><c>Identity/Account/Manage/Index</c> becomes <c>/Identity/Account/Manage</c>; <c>""</c> and <c>Index</c> become <c>/</c>.</summary>
    private static string PagePath(string? rawText)
    {
        string route = (rawText ?? "").Trim('/');
        if (route.Equals("Index", StringComparison.Ordinal))
        {
            route = "";
        }
        else if (route.EndsWith("/Index", StringComparison.Ordinal))
        {
            route = route[..^"/Index".Length];
        }
        return "/" + route;
    }

    /// <summary>The cookie handler's authorization challenge; its <c>Location</c> is absolute.</summary>
    private static bool IsLoginChallenge(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Redirect || response.Headers.Location is not { } location)
        {
            return false;
        }
        string target = location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
        return target.StartsWith(LoginChallengePrefix, StringComparison.Ordinal);
    }

    private static string[] Sorted(IEnumerable<string> values) => values.Order(StringComparer.Ordinal).ToArray();

    /// <summary>A multipart POST with no fields from <paramref name="ip"/>.</summary>
    private static async Task<HttpResponseMessage> PostEmptyFormAsync(HttpClient client, string route, string ip)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/" + route)
        {
            Content = new MultipartFormDataContent()
        };
        request.Headers.Add(WebsiteWebApplicationFactory.RemoteIpHeader, ip);
        return await client.SendAsync(request, Ct);
    }
}
