namespace MobileGnollHackLogger.Security;

/// <summary>
/// Security headers on every website response. Each header is set only when the response does not carry it yet,
/// so a controller's own value (the dumplog sandbox policy) stands, and a pipeline re-executed for an error page
/// adds nothing twice.
/// </summary>
public static class WebsiteSecurityHeaders
{
    public const string ContentSecurityPolicy = "frame-ancestors 'none'";
    public const string PermissionsPolicy =
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

    public static IApplicationBuilder UseWebsiteSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var response = context.Response;
            response.OnStarting(() =>
            {
                // The GnollHack app's WebViews load the site as a top-level page, which the framing headers do not affect.
                response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
                response.Headers.TryAdd("X-Frame-Options", "DENY");
                response.Headers.TryAdd("Content-Security-Policy", ContentSecurityPolicy);
                response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
                response.Headers.TryAdd("Permissions-Policy", PermissionsPolicy);
                return Task.CompletedTask;
            });
            await next(context);
        });
    }
}
