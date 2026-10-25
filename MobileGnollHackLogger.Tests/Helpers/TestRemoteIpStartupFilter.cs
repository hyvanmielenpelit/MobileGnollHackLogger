using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace MobileGnollHackLogger.Tests.Helpers;

/// <summary>
/// First middleware of the pipeline: sets <c>Connection.RemoteIpAddress</c> from the <see cref="HeaderName"/>
/// header, because the test server reports no address and the throttles and rate limits partition by it.
/// </summary>
public sealed class TestRemoteIpStartupFilter : IStartupFilter
{
    public const string HeaderName = "X-Test-Remote-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(HeaderName, out var value)
                    && IPAddress.TryParse(value.ToString(), out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }
                await nextMiddleware();
            });
            next(app);
        };
    }
}
