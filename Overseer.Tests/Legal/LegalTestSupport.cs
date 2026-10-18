using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using GnollHackServer.Data.Legal;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;

namespace Overseer.Tests.Legal;

/// <summary>Fixtures shared by the Terms of Service tests: a temporary wiki folder, the reader, the store and Identity stubs.</summary>
internal static class LegalTestSupport
{
    public const string TermsPage = "Gnoll Overseer Terms of Service";
    public const string TermsFile = TermsPage + ".md";

    /// <summary>The first three lines of the wiki's Gnoll Overseer Terms of Service, as of commit e4d613e.</summary>
    public const string RealFirstLines =
        "**Last updated:** 18 October 2026\n"
        + "\n"
        + "These Terms of Service (\"Terms\") govern your access to and use of **Gnoll Overseer** (the \"Service\"), an optional artificial intelligence (AI) chat assistant provided by **Hyvän mielen pelit ry**, operating in English as **Sound Mind Games** (\"we\", \"us\", or \"our\"). The Service is accessible through the GnollHack video game client and via our web interface at [overseer.gnollhack.com](https://overseer.gnollhack.com).\n";

    public static LegalDocumentOptions Options(LegalVersionSource source = LegalVersionSource.DocumentDate, string gitExecutable = "git")
        => new()
        {
            WikiBaseUrl = "https://wiki.gnollhack.com/",
            VersionSource = source,
            GitExecutable = gitExecutable,
            Documents = new Dictionary<string, LegalDocumentDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                [LegalDocumentKeys.OverseerTerms] = new()
                {
                    WikiPageName = TermsPage,
                    PrivacyPolicyPageName = "Gnoll Overseer Privacy Policy"
                }
            }
        };

    public static IConfiguration Configuration(string? wikiPath)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "WikiPath", wikiPath }
        }).Build();

    public static LegalDocumentReader Reader(string? wikiPath, LegalVersionSource source = LegalVersionSource.DocumentDate, string gitExecutable = "git")
        => new(Microsoft.Extensions.Options.Options.Create(Options(source, gitExecutable)), Configuration(wikiPath),
            NullLogger<LegalDocumentReader>.Instance);

    public static ApplicationDbContext Db(string? name = null)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .Options);

    public static MemoryCache Cache() => new(new MemoryCacheOptions { SizeLimit = 10000 });

    public static TermsAcceptanceStore Store(ApplicationDbContext db, LegalDocumentReader reader, IMemoryCache? cache = null)
        => new(db, reader, cache ?? Cache(), NullLogger<TermsAcceptanceStore>.Instance);

    public static async Task AddAcceptanceAsync(ApplicationDbContext db, string userId, string version, string key = LegalDocumentKeys.OverseerTerms)
    {
        db.UserTermsAcceptances.Add(new UserTermsAcceptance
        {
            AspNetUserId = userId,
            DocumentKey = key,
            Version = version,
            AcceptedUtc = DateTime.UtcNow,
            Source = LegalDocumentKeys.SourceWeb
        });
        await db.SaveChangesAsync();
    }

    public static bool GitAvailable()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process == null) return false;
            process.WaitForExit(10000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Runs git in <paramref name="directory"/> and fails the test when it fails.</summary>
    public static void Git(string directory, params string[] args)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = directory
        };
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(startInfo)!;
        string error = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {error}");
        }
    }

    public static ClaimsPrincipal Principal(string? userId)
        => new(userId == null
            ? new ClaimsIdentity()
            : new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth"));
}

/// <summary>A temporary folder standing in for the wiki clone; deleted on dispose.</summary>
internal sealed class TempWiki : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "overseer-legal-" + Guid.NewGuid().ToString("N"));

    public TempWiki()
    {
        Directory.CreateDirectory(Path);
    }

    public string Write(string content, string fileName = LegalTestSupport.TermsFile)
    {
        string file = System.IO.Path.Combine(Path, fileName);
        File.WriteAllText(file, content, new System.Text.UTF8Encoding(false));
        return file;
    }

    public void Dispose()
    {
        try
        {
            if (!Directory.Exists(Path)) return;
            // Git marks its object files read-only, which Directory.Delete refuses.
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>An Identity user store answering from a dictionary of users.</summary>
internal sealed class DictionaryUserStore : IUserStore<ApplicationUser>
{
    public Dictionary<string, ApplicationUser> Users { get; } = new(StringComparer.Ordinal);

    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
        => Task.FromResult(Users.TryGetValue(userId, out var user) ? user : null);

    public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        foreach (var user in Users.Values)
        {
            if (string.Equals(user.UserName, normalizedUserName, StringComparison.OrdinalIgnoreCase)) return Task.FromResult<ApplicationUser?>(user);
        }
        return Task.FromResult<ApplicationUser?>(null);
    }

    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id);
    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName?.ToUpperInvariant());
    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
    public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
    public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
    public void Dispose() { }
}

internal sealed class DictionaryUserManager : UserManager<ApplicationUser>
{
    public DictionaryUserManager(DictionaryUserStore store)
        : base(store, null!, null!, Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(), null!, null!, null!,
            NullLogger<UserManager<ApplicationUser>>.Instance)
    {
    }
}

/// <summary>Holds the detached greeting task until released, so the started turn stays visible; then makes it fault.</summary>
internal sealed class HeldScopeFactory : IServiceScopeFactory, IDisposable
{
    public ManualResetEventSlim Release { get; } = new(false);

    public IServiceScope CreateScope()
    {
        Release.Wait(TimeSpan.FromSeconds(10));
        throw new InvalidOperationException("Scope creation refused by the test fixture.");
    }

    public void Dispose()
    {
        Release.Set();
    }
}
