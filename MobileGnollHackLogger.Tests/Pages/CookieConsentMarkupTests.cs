using System.Net;
using GnollHackServer.Data.Legal;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Pages;

/// <summary>
/// What the layout renders for the cookie consent, with and without a Tag Manager container ID: the notice and the
/// Cookie Settings button only with an ID, no tag and no third-party font in the HTML either way, and the self-hosted
/// font preloads and the Cookie Policy link always.
/// </summary>
public class CookieConsentMarkupTests
    : IClassFixture<CookieConsentMarkupTests.TagManagerFactory>, IClassFixture<WebsiteWebApplicationFactory>
{
    private const string ContainerId = "GTM-TESTONLY";
    private const byte IpBlock = 0x25;

    private readonly TagManagerFactory _withId;
    private readonly WebsiteWebApplicationFactory _withoutId;

    public CookieConsentMarkupTests(TagManagerFactory withId, WebsiteWebApplicationFactory withoutId)
    {
        _withId = withId;
        _withoutId = withoutId;
    }

    /// <summary>The website with a placeholder Tag Manager container ID.</summary>
    public class TagManagerFactory : WebsiteWebApplicationFactory
    {
        protected override IDictionary<string, string?> Settings
        {
            get
            {
                var settings = base.Settings;
                settings["GoogleTagManagerID"] = ContainerId;
                return settings;
            }
        }
    }

    public static TheoryData<string> Paths => new() { "/", BrowserSession.LoginPath };

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task WithContainerId_RendersNoticeAndSettingsButtonAndNoTag(string path)
    {
        string html = await GetHtmlAsync(_withId, path);

        Assert.Contains("id=\"consentDiv\"", html);
        Assert.Contains("id=\"consentAcceptButton\"", html);
        Assert.Contains("id=\"consentDeclineButton\"", html);
        Assert.Contains("id=\"cookieSettingsButton\"", html);
        Assert.Contains("data-gtm-id=\"" + ContainerId + "\"", html);
        Assert.Equal(1, CountOf(html, ContainerId));
        AssertNoThirdPartyTagOrFont(html);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task WithoutContainerId_RendersNoNoticeAndNoSettingsButton(string path)
    {
        string html = await GetHtmlAsync(_withoutId, path);

        Assert.DoesNotContain("id=\"consentDiv\"", html);
        Assert.DoesNotContain("id=\"cookieSettingsButton\"", html);
        AssertNoThirdPartyTagOrFont(html);
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task BothConfigurations_RenderFontPreloadsAndCookiePolicyLink(string path)
    {
        foreach (var factory in new[] { _withId, _withoutId })
        {
            string html = await GetHtmlAsync(factory, path);
            string cookiePolicyUrl = await factory.WithScopeAsync(sp =>
                Task.FromResult(sp.GetRequiredService<LegalDocumentReader>().PageUrl("GnollHack Account Cookie Policy")));

            Assert.Contains("<link rel=\"preload\" href=\"/fonts/cinzel-latin-wght-normal.woff2\" as=\"font\" type=\"font/woff2\" crossorigin", html);
            Assert.Contains("<link rel=\"preload\" href=\"/fonts/lato2-latin-400-normal.woff2\" as=\"font\" type=\"font/woff2\" crossorigin", html);
            Assert.Contains("href=\"" + cookiePolicyUrl + "\">Cookie Policy</a>", html);
        }
    }

    private static async Task<string> GetHtmlAsync(WebsiteWebApplicationFactory factory, string path)
    {
        using var session = new BrowserSession(factory, BrowserSession.NextIp(IpBlock));
        var response = await session.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static void AssertNoThirdPartyTagOrFont(string html)
    {
        Assert.DoesNotContain("googletagmanager.com", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<noscript", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.googleapis.com", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.gstatic.com", html, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
