using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MobileGnollHackLogger.Tests.Helpers;

namespace MobileGnollHackLogger.Tests.Security;

/// <summary>
/// The website's Data Protection key ring: persisted to <c>PrivacySettings:DataProtectionKeysPath</c> under the
/// application name <c>GnollHackAccount</c>, so a restart keeps every sign-in, antiforgery and TempData cookie and
/// every emailed token readable, and a missing setting in Staging or Production is logged at Error.
/// </summary>
public sealed class DataProtectionKeyRingTests : IDisposable
{
    private const string SettingName = "PrivacySettings:DataProtectionKeysPath";
    private const string Purpose = "ACCOUNT-7 regression";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "WebsiteKeyRingTests_" + Guid.NewGuid().ToString("N"));

    public DataProtectionKeyRingTests()
    {
        Directory.CreateDirectory(_root);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The website with <see cref="SettingName"/> set to <c>keysPath</c>, or explicitly empty when it is <c>null</c>,
    /// in the given environment, with every log entry recorded with its level.
    /// </summary>
    private sealed class KeyRingFactory(string? keysPath, string environment = "Testing") : WebsiteWebApplicationFactory
    {
        public LevelRecordingLoggerProvider LevelLogs { get; } = new();

        protected override IDictionary<string, string?> Settings
        {
            get
            {
                var settings = base.Settings;
                settings[SettingName] = keysPath ?? "";
                return settings;
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment(environment);
            builder.ConfigureLogging(logging =>
            {
                logging.AddProvider(LevelLogs);
                logging.AddFilter<LevelRecordingLoggerProvider>(_ => true);
            });
        }

        public bool LoggedMissingKeysPathError() =>
            LevelLogs.Entries.Any(e => e.Level == LogLevel.Error && e.Message.Contains(SettingName, StringComparison.Ordinal));
    }

    /// <summary>Records every host log message with its level.</summary>
    private sealed class LevelRecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) => new LevelRecordingLogger(_entries);

        public void Dispose()
        {
        }

        private sealed class LevelRecordingLogger(ConcurrentQueue<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }

    private string KeyFolder(string name) => Path.Combine(_root, name);

    private static IDataProtector Protector(WebsiteWebApplicationFactory factory) =>
        factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);

    [Fact]
    public async Task KeyRing_IsPersistedToTheConfiguredFolder()
    {
        string folder = KeyFolder("persisted");
        using var factory = new KeyRingFactory(folder);
        using var client = factory.CreateBrowserClient(BrowserSession.NextIp(0x70));

        var response = await client.GetAsync("/Identity/Account/Login", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(Directory.Exists(folder));
        Assert.NotEmpty(Directory.GetFiles(folder, "key-*.xml"));
    }

    [Fact]
    public void KeyRing_SurvivesAHostRestart()
    {
        string folder = KeyFolder("restart");
        string protectedText;
        using (var hostA = new KeyRingFactory(folder))
        {
            protectedText = Protector(hostA).Protect("signed in");
        }

        using var hostB = new KeyRingFactory(folder);

        Assert.Equal("signed in", Protector(hostB).Unprotect(protectedText));
    }

    [Fact]
    public void KeyRing_FromAnotherFolder_CannotUnprotect()
    {
        string protectedText;
        using (var hostA = new KeyRingFactory(KeyFolder("x")))
        {
            protectedText = Protector(hostA).Protect("signed in");
        }

        using var hostB = new KeyRingFactory(KeyFolder("y"));
        var protector = Protector(hostB);

        Assert.Throws<CryptographicException>(() => protector.Unprotect(protectedText));
    }

    [Fact]
    public void ApplicationName_IsGnollHackAccount()
    {
        using var factory = new KeyRingFactory(KeyFolder("name"));

        var options = factory.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value;

        Assert.Equal("GnollHackAccount", options.ApplicationDiscriminator);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void MissingKeysPath_LogsAnError(string environment)
    {
        using var factory = new KeyRingFactory(null, environment);
        using var client = factory.CreateClient();

        Assert.True(factory.LoggedMissingKeysPathError());
    }

    [Fact]
    public void KeysPathSet_LogsNoSuchError()
    {
        using var factory = new KeyRingFactory(KeyFolder("staging"), "Staging");
        using var client = factory.CreateClient();

        Assert.False(factory.LoggedMissingKeysPathError());
    }

    [Fact]
    public void MissingKeysPath_InTesting_LogsNoSuchError()
    {
        using var factory = new KeyRingFactory(null);
        using var client = factory.CreateClient();

        Assert.False(factory.LoggedMissingKeysPathError());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
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
