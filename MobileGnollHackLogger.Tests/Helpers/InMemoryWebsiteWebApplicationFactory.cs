namespace MobileGnollHackLogger.Tests.Helpers;

/// <summary>The website host on the InMemory provider, for code that compares <c>ulong</c> columns.</summary>
public class InMemoryWebsiteWebApplicationFactory : WebsiteWebApplicationFactory
{
    protected override bool UseInMemoryProvider => true;
}
