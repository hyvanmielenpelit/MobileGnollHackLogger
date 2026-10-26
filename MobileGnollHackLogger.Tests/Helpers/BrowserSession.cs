using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace MobileGnollHackLogger.Tests.Helpers;

/// <summary>
/// A browser against the Razor pages: one cookie jar, one remote address, real antiforgery tokens read from the
/// rendered forms. Redirects are not followed.
/// </summary>
public sealed partial class BrowserSession : IDisposable
{
    public const string LoginPath = "/Identity/Account/Login";

    private static int _ipCounter;

    public BrowserSession(WebsiteWebApplicationFactory factory, string remoteIp, string baseAddress = "https://localhost")
    {
        RemoteIp = remoteIp;
        Client = factory.CreateBrowserClient(remoteIp, baseAddress);
    }

    public HttpClient Client { get; }

    public string RemoteIp { get; }

    /// <summary>A documentation address <c>2001:db8:b&lt;block&gt;::...</c> no other call in this process returns.</summary>
    public static string NextIp(byte block)
    {
        int n = Interlocked.Increment(ref _ipCounter);
        return string.Format(CultureInfo.InvariantCulture, "2001:db8:b{0:x}::{1:x}:{2:x}", block, (n >> 16) & 0xffff, n & 0xffff);
    }

    public Task<HttpResponseMessage> GetAsync(string path) => Client.GetAsync(path, TestContext.Current.CancellationToken);

    /// <summary>GETs <paramref name="path"/> and returns the value of the first <c>__RequestVerificationToken</c> field it renders.</summary>
    public async Task<string> GetFormTokenAsync(string path)
    {
        var response = await GetAsync(path);
        string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        foreach (Match input in InputTagRegex().Matches(html))
        {
            if (!TokenNameRegex().IsMatch(input.Value))
            {
                continue;
            }
            var value = ValueAttributeRegex().Match(input.Value);
            if (value.Success)
            {
                return WebUtility.HtmlDecode(value.Groups[1].Value);
            }
        }
        throw new InvalidOperationException("No __RequestVerificationToken on " + path + " (status "
            + (int)response.StatusCode + ").");
    }

    /// <summary>
    /// POSTs <paramref name="fields"/> as a form. The token comes from a GET of <paramref name="tokenFromPath"/>, or of
    /// <paramref name="path"/> when that is null; <paramref name="includeToken"/> false sends no token at all.
    /// </summary>
    public async Task<HttpResponseMessage> PostFormAsync(string path, IDictionary<string, string> fields, bool includeToken = true,
        string? tokenFromPath = null)
    {
        var form = new List<KeyValuePair<string, string>>(fields);
        if (includeToken)
        {
            form.Add(new("__RequestVerificationToken", await GetFormTokenAsync(tokenFromPath ?? path)));
        }
        using var content = new FormUrlEncodedContent(form);
        return await Client.PostAsync(path, content, TestContext.Current.CancellationToken);
    }

    /// <summary>Signs in through the Login page and asserts that the sign-in completed.</summary>
    public async Task SignInAsync(string userName, string password)
    {
        var response = await PostFormAsync(LoginPath, new Dictionary<string, string>
        {
            ["Input.UserName"] = userName,
            ["Input.Password"] = password
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        string location = response.Headers.Location?.OriginalString ?? "";
        Assert.False(location.StartsWith(LoginPath, StringComparison.OrdinalIgnoreCase), "Sign-in redirected to " + location);
        Assert.False(location.StartsWith("/Identity/Account/Lockout", StringComparison.OrdinalIgnoreCase), "Sign-in redirected to " + location);
    }

    /// <summary>
    /// The first <c>href</c> in <paramref name="htmlMessage"/> whose value contains <paramref name="pagePath"/>, HTML-decoded:
    /// the page models encode the link with <c>HtmlEncoder</c>, so <c>&amp;</c> arrives as <c>&amp;amp;</c>.
    /// </summary>
    public static string LinkIn(string htmlMessage, string pagePath)
    {
        foreach (Match href in HrefRegex().Matches(htmlMessage))
        {
            string value = WebUtility.HtmlDecode(href.Groups[2].Value);
            if (value.Contains(pagePath, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }
        throw new InvalidOperationException("No link to " + pagePath + " in the message.");
    }

    public void Dispose() => Client.Dispose();

    [GeneratedRegex(@"<input\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InputTagRegex();

    [GeneratedRegex(@"\bname\s*=\s*[""']__RequestVerificationToken[""']", RegexOptions.IgnoreCase)]
    private static partial Regex TokenNameRegex();

    [GeneratedRegex(@"\bvalue\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex ValueAttributeRegex();

    [GeneratedRegex(@"\bhref\s*=\s*([""'])(.*?)\1", RegexOptions.IgnoreCase)]
    private static partial Regex HrefRegex();
}
