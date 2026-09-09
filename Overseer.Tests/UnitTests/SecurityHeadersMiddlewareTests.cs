using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Overseer.Middleware;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class SecurityHeadersMiddlewareTests
{
    private static async Task<HttpResponse> InvokeAsync(Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();

        var context = new DefaultHttpContext();
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask, configuration);
        await middleware.InvokeAsync(context);
        return context.Response;
    }

    [Fact]
    public async Task Emits_AllSecurityHeaders()
    {
        var response = await InvokeAsync();

        Assert.Equal("nosniff", response.Headers["X-Content-Type-Options"]);
        Assert.Equal("no-referrer", response.Headers["Referrer-Policy"]);
        Assert.Equal("DENY", response.Headers["X-Frame-Options"]);
        Assert.False(string.IsNullOrEmpty(response.Headers["Permissions-Policy"]));
        Assert.False(string.IsNullOrEmpty(response.Headers["Content-Security-Policy"]));
    }

    [Fact]
    public async Task ContentSecurityPolicy_CarriesTheDirectivesTheClientActuallyNeeds()
    {
        var response = await InvokeAsync();
        string csp = response.Headers["Content-Security-Policy"]!;

        Assert.Contains("default-src 'self'", csp);

        /* connect-src must include 'self' or every /api/* call and the Sentry tunnel is
           blocked; ws:/wss: because 'self' does not reliably match the WebSocket scheme, and
           SignalR needs it. */
        Assert.Contains("connect-src 'self' ws: wss:", csp);

        // Angular injects component styles at runtime and index.html has an inline <style>.
        Assert.Contains("style-src 'self' 'unsafe-inline'", csp);

        Assert.Contains("font-src 'self' data:", csp);
        Assert.Contains("img-src 'self' data:", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("base-uri 'self'", csp);
        Assert.Contains("form-action 'self'", csp);
    }

    [Fact]
    public async Task ContentSecurityPolicy_HasNoRemoteSources()
    {
        var response = await InvokeAsync();
        string csp = response.Headers["Content-Security-Policy"]!;

        /* The self-hosted fonts are the reason font-src can be 'self': a reintroduced Google
           Fonts link must fail loudly rather than quietly working. */
        Assert.DoesNotContain("fonts.googleapis.com", csp);
        Assert.DoesNotContain("fonts.gstatic.com", csp);
        Assert.DoesNotContain("http://", csp);
        Assert.DoesNotContain("https://", csp);
    }

    [Fact]
    public async Task ContentSecurityPolicy_AllowsNeitherUnsafeEvalNorBlob()
    {
        var response = await InvokeAsync();
        string csp = response.Headers["Content-Security-Policy"]!;

        // Production Angular is AOT, and the client's only createObjectURL sites feed a[download].
        Assert.DoesNotContain("'unsafe-eval'", csp);
        Assert.DoesNotContain("blob:", csp);

        // script-src carries 'self' alone -- no inline scripts anywhere, handoff page included.
        Assert.Contains("script-src 'self';", csp);
    }

    [Fact]
    public async Task ContentSecurityPolicy_CanBeOverriddenFromConfiguration()
    {
        var response = await InvokeAsync(new Dictionary<string, string?>
        {
            { "PrivacySettings:ContentSecurityPolicy", "default-src 'none'" }
        });

        Assert.Equal("default-src 'none'", response.Headers["Content-Security-Policy"]);
    }
}
