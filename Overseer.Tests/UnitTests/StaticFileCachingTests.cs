using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Cache-Control on static files, and response compression over the whole pipeline.
/// </summary>
public class StaticFileCachingTests : IClassFixture<OverseerWebApplicationFactory>
{
    private const string Immutable = "public, max-age=31536000, immutable";

    private readonly OverseerWebApplicationFactory _factory;

    public StaticFileCachingTests(OverseerWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string? CacheControlFor(string path)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;
        var context = new StaticFileResponseContext(httpContext, new NotFoundFileInfo(path.Split('/').Last()));

        Program.ApplyStaticFileCacheHeaders(context);

        string? value = httpContext.Response.Headers.CacheControl;
        return string.IsNullOrEmpty(value) ? null : value;
    }

    [Theory]
    [InlineData("/main-ABCDEFGH.js")]
    [InlineData("/polyfills-ABCDEFGH.js")]
    [InlineData("/chunk-E_4PHPkC.js")]
    [InlineData("/chunk-B-M0GfOF.js")]
    [InlineData("/styles-GIKIKYHK.css")]
    [InlineData("/media/KaTeX_AMS-Regular-CYEKBG2K.woff2")]
    public void HashedBundlesAndMedia_AreImmutable(string path)
    {
        Assert.Equal(Immutable, CacheControlFor(path));
    }

    [Fact]
    public void IndexHtml_IsNeverCached()
    {
        Assert.Equal("no-cache, no-store", CacheControlFor("/index.html"));
    }

    [Theory]
    [InlineData("/fonts/x.woff2")]
    [InlineData("/pdfjs/x.mjs")]
    [InlineData("/js/x.js")]
    [InlineData("/workers/main-ABCDEFGH.js")]
    [InlineData("/main.js")]
    [InlineData("/main-2755LJJ6.js.map")]
    [InlineData("/manifest.webmanifest")]
    public void UnhashedOrNestedFiles_GetNoCacheControl(string path)
    {
        Assert.Null(CacheControlFor(path));
    }

    [Theory]
    [InlineData("br")]
    [InlineData("gzip")]
    public async Task JsonApiResponse_IsCompressedWithTheRequestedEncoding(string encoding)
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/settings");
        request.Headers.Add("X-Test-User", "compression-user");
        request.Headers.Add("Accept-Encoding", encoding);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"GET /api/settings answered {(int)response.StatusCode}.");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new[] { encoding }, response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task SpaDeepLink_IsNeverCached()
    {
        // wwwroot is empty on a clean checkout, so the fallback may answer 404; the header is set before it runs.
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/chat/some-session", TestContext.Current.CancellationToken);

        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }
}
