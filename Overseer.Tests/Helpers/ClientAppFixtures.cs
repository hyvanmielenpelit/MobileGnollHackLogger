using System;
using System.IO;
using Xunit;

namespace Overseer.Tests.Helpers;

/// <summary>The JSON fixtures the Angular client's specs and these tests both read, located from the repository root.</summary>
internal static class ClientAppFixtures
{
    /// <summary>The path of <paramref name="fileName"/> in the client's chat consistency tab folder.</summary>
    internal static string ChatConsistencyFixture(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "MobileGnollHackLogger.slnx")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "Overseer", "ClientApp", "src", "app", "admin", "benchmark", "chat-consistency-tab", fileName);
    }
}
