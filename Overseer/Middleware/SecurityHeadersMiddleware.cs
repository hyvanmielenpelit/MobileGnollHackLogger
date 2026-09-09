using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Overseer.Middleware;

/// <summary>
/// Emits the response security headers every Overseer response carries.
/// </summary>
/// <remarks>
/// Registered before UseStaticFiles, and therefore before UseRouting, so
/// HttpContext.GetEndpoint() is null here and the policy cannot vary per endpoint. That is
/// deliberate: one global policy. A per-response nonce would require moving the registration
/// after UseRouting.
/// </remarks>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _contentSecurityPolicy;

    /* style-src keeps 'unsafe-inline' because Angular injects component styles at runtime and
       index.html carries an inline <style> for the pre-bootstrap shell.

       connect-src must include 'self': it governs every /api/* call and the Sentry tunnel at
       /api/sentry/log. ws:/wss: are listed separately because 'self' does not reliably match
       the WebSocket scheme across browsers, and SignalR needs it.

       img-src without a remote source is half of the markdown exfiltration fix.

       No 'unsafe-eval': production Angular is AOT. Do not add it for a development-mode error.
       No blob:: the client's two URL.createObjectURL sites both feed an a[download].
       manifest-src, worker-src, frame-src and media-src fall back to default-src 'self'. */
    private const string DefaultPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "font-src 'self' data:; " +
        "img-src 'self' data:; " +
        "connect-src 'self' ws: wss:; " +
        "frame-ancestors 'none'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'";

    public SecurityHeadersMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        /* An override exists so a deployment can widen the policy without a code change --
           for example to re-allowlist a font or script host -- but the shipped value is the
           enforcing policy above, never report-only. */
        _contentSecurityPolicy = configuration["PrivacySettings:ContentSecurityPolicy"] is { Length: > 0 } configured
            ? configured
            : DefaultPolicy;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        /* Assigned rather than appended: a second value for any of these is either a duplicate
           or a conflicting policy, and for CSP the browser enforces the intersection of both. */
        headers["Content-Security-Policy"] = _contentSecurityPolicy;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Frame-Options"] = "DENY";
        headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

        return _next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();
}
