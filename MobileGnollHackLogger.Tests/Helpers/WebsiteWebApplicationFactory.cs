using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Helpers;

/// <summary>
/// The website host in the <c>Testing</c> environment, with its own database (SQLite unless
/// <see cref="UseInMemoryProvider"/>) and a temporary directory for dumplogs, bones and the wiki.
/// One instance per test class, through <c>IClassFixture</c>.
/// </summary>
public class WebsiteWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string AntiForgeryToken = "test-antiforgery-secret";
    public const string EncryptionKeyString = "0123456789abcdef0123456789abcdef";
    public const string EncryptionIVString = "fedcba9876543210";
    public const string RemoteIpHeader = TestRemoteIpStartupFilter.HeaderName;

    private SqliteConnection? _connection;
    private readonly string _inMemoryDatabaseName = "WebsiteTests_" + Guid.NewGuid().ToString("N");

    public WebsiteWebApplicationFactory()
    {
        TempRoot = Path.Combine(Path.GetTempPath(), "WebsiteTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DumpLogPath);
        Directory.CreateDirectory(BonesPath);
        Directory.CreateDirectory(WikiPath);
    }

    public string TempRoot { get; }
    public string DumpLogPath => Path.Combine(TempRoot, "dumplogs");
    public string BonesPath => Path.Combine(TempRoot, "bones");
    public string WikiPath => Path.Combine(TempRoot, "wiki");

    /// <summary>Every email the host sent.</summary>
    public RecordingEmailSender Emails { get; } = new();

    /// <summary>The SQLite provider cannot translate comparisons of <c>ulong</c> columns; the InMemory provider can.</summary>
    protected virtual bool UseInMemoryProvider => false;

    /// <summary>
    /// Host settings. They reach <c>Program</c> as command-line arguments, so they are visible to the reads it makes
    /// before <c>builder.Build()</c> and outrank every JSON file and environment variable.
    /// </summary>
    protected virtual IDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["ConnectionStrings:SqlDatabaseConnection"] = "Server=unused;Database=unused",
        ["ConnectionStrings:EmailConnection"] = "endpoint=https://email.example.invalid/;accesskey=dGVzdA==",
        ["AntiForgeryToken"] = AntiForgeryToken,
        ["DumpLogPath"] = DumpLogPath,
        ["BonesPath"] = BonesPath,
        ["WikiPath"] = WikiPath,
        ["EncryptionKeyString"] = EncryptionKeyString,
        ["EncryptionIVString"] = EncryptionIVString,
        // An empty DSN disables the Sentry SDK.
        ["SentryDSN"] = "",
        ["BonesVersionCompatibilityInfo:0:Version"] = "1",
        ["BonesVersionCompatibilityInfo:0:Label"] = "Test pool 1",
        ["BonesVersionCompatibilityInfo:1:Version"] = "9000",
        ["BonesVersionCompatibilityInfo:1:Label"] = "Test pool 2",
    };

    /// <summary>Extra options for the test database, such as an interceptor.</summary>
    protected virtual void ConfigureDbContextOptions(DbContextOptionsBuilder<ApplicationDbContext> options)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // User secrets load only in Development; Testing keeps the developer's own website secrets out of the host.
        builder.UseEnvironment("Testing");

        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value ?? "");
        }

        // Host logs go to the running test's output, which the test runner shows only for a failed test.
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(new TestContextLoggerProvider());
        });

        // Requests run in the calling test's execution context, so TestContext.Current names that test.
        builder.UseTestServer(o => o.PreserveExecutionContext = true);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();

            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            if (UseInMemoryProvider)
            {
                optionsBuilder.UseInMemoryDatabase(_inMemoryDatabaseName);
            }
            else
            {
                _connection ??= OpenSqliteDatabase();
                optionsBuilder.UseSqlite(_connection);
            }
            ConfigureDbContextOptions(optionsBuilder);
            services.AddSingleton(optionsBuilder.Options);

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);

            services.AddTransient<IStartupFilter, TestRemoteIpStartupFilter>();
        });
    }

    /// <summary>
    /// A private copy of the schema. Column defaults of the model call SQL Server's <c>getutcdate()</c>, which SQLite
    /// evaluates on insert, so the connection gets a function of that name in the text format EF Core's SQLite provider reads.
    /// </summary>
    private static SqliteConnection OpenSqliteDatabase()
    {
        var connection = SqliteSchemaTemplate.OpenCopy();
        connection.CreateFunction("getutcdate",
            () => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", System.Globalization.CultureInfo.InvariantCulture),
            isDeterministic: false);
        return connection;
    }

    /// <summary>A client for the game APIs: HTTPS, so <c>UseHttpsRedirection</c> stays out of the way, and no redirects followed.</summary>
    public HttpClient CreateGameClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    public async Task WithScopeAsync(Func<IServiceProvider, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        _connection?.Dispose();
        _connection = null;
        try
        {
            if (Directory.Exists(TempRoot))
            {
                Directory.Delete(TempRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
