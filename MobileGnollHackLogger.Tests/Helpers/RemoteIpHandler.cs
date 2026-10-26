namespace MobileGnollHackLogger.Tests.Helpers;

/// <summary>Adds the <see cref="TestRemoteIpStartupFilter.HeaderName"/> header to every request that does not carry one.</summary>
public sealed class RemoteIpHandler(string remoteIp) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(TestRemoteIpStartupFilter.HeaderName))
        {
            request.Headers.Add(TestRemoteIpStartupFilter.HeaderName, remoteIp);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
