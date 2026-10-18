using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Overseer.Services;
using Overseer.Services.ConfigHealth;
using Overseer.Services.SourceIndexing;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The Configuration Health report: which provider a value comes from, secret placement, that no sensitive
/// value ever reaches the report, path, limit and makedefs checks. The configuration is built from a real
/// appsettings.json and secrets.json in a temporary folder, plus in-memory values.
/// </summary>
public sealed class ConfigHealthReportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "overseer-config-health-" + Guid.NewGuid().ToString("N"));

    public ConfigHealthReportTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // -- helpers -------------------------------------------------------------------------------------

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Overseer";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>A ConfigHealthService whose makedefs state is given instead of read from SourceCodeService.</summary>
    private sealed class MakedefsStateHealthService : ConfigHealthService
    {
        private readonly MakedefsHealthState? _state;

        public MakedefsStateHealthService(IConfiguration configuration, MakedefsHealthState? state)
            : base(configuration)
        {
            _state = state;
        }

        protected override MakedefsHealthState? GetMakedefsState() => _state;
    }

    /// <summary>Writes flat <c>A:B</c> keys as a nested JSON file under the temporary folder.</summary>
    private string WriteSettings(string relativePath, IReadOnlyDictionary<string, string>? values)
    {
        var root = new Dictionary<string, object>();
        foreach (var (key, value) in values ?? new Dictionary<string, string>())
        {
            string[] parts = key.Split(':');
            var node = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!node.TryGetValue(parts[i], out var child))
                {
                    child = new Dictionary<string, object>();
                    node[parts[i]] = child;
                }

                node = (Dictionary<string, object>)child;
            }

            node[parts[^1]] = value;
        }

        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(root));
        return path;
    }

    private IConfigurationRoot Build(
        Dictionary<string, string>? appSettings = null,
        Dictionary<string, string>? secrets = null,
        Dictionary<string, string?>? memory = null)
    {
        var builder = new ConfigurationBuilder()
            .AddJsonFile(WriteSettings("appsettings.json", appSettings), optional: false, reloadOnChange: false)
            .AddJsonFile(WriteSettings(Path.Combine("user-secrets", "secrets.json"), secrets), optional: false, reloadOnChange: false);
        if (memory != null)
        {
            builder.AddInMemoryCollection(memory);
        }

        return builder.Build();
    }

    private static ConfigHealthReportDto Report(IConfiguration configuration, IHostEnvironment? environment = null)
        => new ConfigHealthService(configuration, null, null, null, environment).GetHealthReport();

    private static ConfigHealthCheckDto Check(ConfigHealthReportDto report, string id)
        => Assert.Single(report.Checks, c => c.Id == id);

    private string MakeDirectory(params string[] parts)
    {
        string path = Path.Combine(new[] { _root }.Concat(parts).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    private string MakeFile(params string[] parts)
    {
        string path = Path.Combine(new[] { _root }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    private static string Base64Key(int firstByte)
        => Convert.ToBase64String(Enumerable.Range(firstByte, 32).Select(b => (byte)b).ToArray());

    // -- source attribution --------------------------------------------------------------------------

    [Fact]
    public void SourceAttribution_NamesTheProviderWhoseValueWins()
    {
        var configuration = Build(
            appSettings: new() { ["WikiPath"] = "a", ["KbPath"] = "a", ["PrivacySettings:ActiveKeyVersion"] = "v1" },
            secrets: new() { ["KbPath"] = "b", ["PrivacySettings:KeyRing:v1"] = Base64Key(1) },
            memory: new() { ["NetHackWikiPath"] = "c" });

        var inspector = new ConfigurationSourceInspector(configuration);

        Assert.Equal("appsettings.json", inspector.Describe("WikiPath"));
        Assert.Equal("User Secrets", inspector.Describe("KbPath"));
        Assert.Equal("User Secrets", inspector.Describe("PrivacySettings:KeyRing"));
        Assert.Equal("MemoryConfigurationProvider", inspector.Describe("NetHackWikiPath"));
        Assert.Equal("Not set", inspector.Describe("DumpLogPath"));

        var report = Report(configuration);
        Assert.Equal("appsettings.json", Check(report, "wiki-path").Source);
        Assert.Equal("User Secrets", Check(report, "kb-path").Source);
        Assert.Equal("Not set", Check(report, "dumplog-path").Source);
        Assert.Equal("User Secrets, appsettings.json", Check(report, "content-keyring").Source);
    }

    [Fact]
    public void Sources_ListEachProviderWithItsKeyCountOnly()
    {
        var configuration = Build(
            appSettings: new() { ["WikiPath"] = "a", ["KbPath"] = "a", ["PrivacySettings:ActiveKeyVersion"] = "v1" },
            secrets: new() { ["AesEncryptionKey"] = Base64Key(1), ["ConnectionStrings:SqlDatabaseConnection"] = "leakprobe-sources" },
            memory: new() { ["NetHackWikiPath"] = "c" });

        var sources = Report(configuration).Sources;

        Assert.Collection(sources,
            s =>
            {
                Assert.Equal("appsettings.json", s.Name);
                Assert.Equal("appsettings", s.Kind);
                Assert.True(s.FileExists);
                Assert.Equal(3, s.KeyCount);
            },
            s =>
            {
                Assert.Equal("User Secrets", s.Name);
                Assert.Equal("userSecrets", s.Kind);
                Assert.True(s.FileExists);
                Assert.Equal(2, s.KeyCount);
            },
            s =>
            {
                Assert.Equal("MemoryConfigurationProvider", s.Name);
                Assert.Equal("memory", s.Kind);
                Assert.Null(s.FileExists);
                Assert.Equal(1, s.KeyCount);
            });
    }

    [Fact]
    public void Sources_ReportAMissingOptionalFile()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(_root, "appsettings.Development.json"), optional: true, reloadOnChange: false)
            .Build();

        var source = Assert.Single(Report(configuration).Sources);

        Assert.Equal("appsettings.Development.json", source.Name);
        Assert.Equal("appsettings", source.Kind);
        Assert.False(source.FileExists);
        Assert.Equal(0, source.KeyCount);
    }

    // -- secret placement ----------------------------------------------------------------------------

    [Fact]
    public void ASecretInAppSettings_IsAnError()
    {
        var configuration = Build(appSettings: new() { ["AesEncryptionKey"] = Base64Key(1) });

        var report = Report(configuration);

        var placement = Check(report, "secret-placement:AesEncryptionKey");
        Assert.Equal(ConfigHealthStatus.Error, placement.Status);
        Assert.Equal(ConfigHealthCatalog.SecretPlacement, placement.Category);
        Assert.Contains("appsettings.json", placement.Summary, StringComparison.Ordinal);
        Assert.NotNull(placement.Remedy);
        Assert.Null(placement.Value);

        var key = Check(report, "aes-encryption-key");
        Assert.Equal(ConfigHealthStatus.Ok, key.Status);
        Assert.Null(key.Value);
    }

    [Fact]
    public void ASecretInAppSettings_IsAnError_EvenWhenUserSecretsOverrideIt()
    {
        var configuration = Build(
            appSettings: new() { ["ConnectionStrings:SqlDatabaseConnection"] = "leakprobe-committed" },
            secrets: new() { ["ConnectionStrings:SqlDatabaseConnection"] = "leakprobe-secret" });

        var placement = Check(Report(configuration), "secret-placement:ConnectionStrings:SqlDatabaseConnection");

        Assert.Equal(ConfigHealthStatus.Error, placement.Status);
        Assert.Equal("User Secrets", placement.Source);
    }

    private const string SaltKey = "PromptCacheSettings:PromptCacheKeySalt";

    [Fact]
    public void TheBuiltInPromptCacheSaltInAppSettings_IsInfo()
    {
        var configuration = Build(appSettings: new() { [SaltKey] = ChatService.DefaultPromptCacheKeySalt });

        var placement = Check(Report(configuration), "secret-placement:" + SaltKey);

        Assert.Equal(ConfigHealthStatus.Info, placement.Status);
        Assert.Contains("built-in default", placement.Summary, StringComparison.Ordinal);
        Assert.Null(placement.Value);
    }

    [Fact]
    public void AnyOtherPromptCacheSaltInAppSettings_IsAnError()
    {
        var configuration = Build(appSettings: new() { [SaltKey] = "leakprobe-salt" });

        var placement = Check(Report(configuration), "secret-placement:" + SaltKey);

        Assert.Equal(ConfigHealthStatus.Error, placement.Status);
        Assert.Null(placement.Value);
    }

    [Fact]
    public void AnOwnPromptCacheSaltInUserSecrets_OverTheBuiltInDefault_IsOk()
    {
        var configuration = Build(
            appSettings: new() { [SaltKey] = ChatService.DefaultPromptCacheKeySalt },
            secrets: new() { [SaltKey] = "leakprobe-own-salt" });

        var placement = Check(Report(configuration), "secret-placement:" + SaltKey);

        Assert.Equal(ConfigHealthStatus.Ok, placement.Status);
        Assert.Equal("User Secrets", placement.Source);
    }

    [Fact]
    public void ASecretInUserSecrets_IsOk_WithNoValue()
    {
        var configuration = Build(
            appSettings: new() { ["GitHub:PersonalAccessToken"] = "" },
            secrets: new()
            {
                ["AesEncryptionKey"] = Base64Key(1),
                ["SentryDSN"] = "leakprobe-dsn",
                ["PrivacySettings:KeyRing:v1"] = Base64Key(40),
                ["PrivacySettings:ActiveKeyVersion"] = "v1"
            });

        var report = Report(configuration);

        foreach (string id in new[] { "secret-placement:AesEncryptionKey", "secret-placement:SentryDSN", "secret-placement:PrivacySettings:KeyRing" })
        {
            var placement = Check(report, id);
            Assert.Equal(ConfigHealthStatus.Ok, placement.Status);
            Assert.Equal("User Secrets", placement.Source);
            Assert.Null(placement.Value);
            Assert.Null(placement.Remedy);
        }

        // An empty placeholder in appsettings.json holds nothing to move.
        Assert.Equal(ConfigHealthStatus.Ok, Check(report, "secret-placement:GitHub:PersonalAccessToken").Status);

        Assert.Equal(ConfigHealthStatus.Ok, Check(report, "sentry-dsn").Status);
        Assert.Null(Check(report, "sentry-dsn").Value);
        Assert.Equal(ConfigHealthStatus.Ok, Check(report, "content-keyring").Status);
        Assert.Null(Check(report, "content-keyring").Value);
    }

    [Fact]
    public void TheReport_NeverContainsASensitiveValue()
    {
        string aesKey = Base64Key(1);
        string ringKey = Base64Key(70);
        var secrets = new[]
        {
            "Server=leakprobe-sql;Password=leakprobe-pass-0001",
            "leakprobe-email-0002",
            "leakprobe-ai-0003",
            "leakprobe-ai-google-0004",
            "leakprobe-github-0005",
            "leakprobe-salt-0006",
            "https://leakprobe-0007@sentry.example/1",
            "leakprobe-antiforgery-0008",
            "leakprobe-other-0009",
            "leakprobe-badkey-0010",
            aesKey,
            ringKey
        };

        var configuration = Build(
            appSettings: new()
            {
                ["ConnectionStrings:SqlDatabaseConnection"] = secrets[0],
                ["AI:APIKey"] = secrets[2],
                ["PromptCacheSettings:PromptCacheKeySalt"] = secrets[5],
                ["AesEncryptionKey"] = aesKey
            },
            secrets: new()
            {
                ["ConnectionStrings:EmailConnection"] = secrets[1],
                ["AI:Google:APIKey"] = secrets[3],
                ["PrivacySettings:KeyRing:v1"] = ringKey,
                ["PrivacySettings:KeyRing:v2"] = secrets[9],
                ["PrivacySettings:ActiveKeyVersion"] = "v1"
            },
            memory: new()
            {
                ["GitHub:PersonalAccessToken"] = secrets[4],
                ["SentryDSN"] = secrets[6],
                ["AntiForgeryToken"] = secrets[7],
                ["ConnectionStrings:Other"] = secrets[8]
            });

        var report = Report(configuration, new TestEnvironment());

        foreach (var options in new[]
        {
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
        })
        {
            string json = JsonSerializer.Serialize(report, options);

            Assert.DoesNotContain("leakprobe", json, StringComparison.OrdinalIgnoreCase);
            foreach (string secret in secrets)
            {
                Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
                Assert.DoesNotContain(secret[..12], json, StringComparison.Ordinal);
            }
        }

        // The checks that found them still ran.
        Assert.Equal(ConfigHealthStatus.Error, Check(report, "secret-placement:AI:APIKey").Status);
        Assert.Equal(ConfigHealthStatus.Ok, Check(report, "secret-placement:AI:Google:APIKey").Status);
        Assert.Equal(ConfigHealthStatus.Ok, Check(report, "secret-placement:ConnectionStrings:Other").Status);
        Assert.Equal(ConfigHealthStatus.Error, Check(report, "content-keyring").Status);
        Assert.All(report.Checks.Where(c => c.Keys.Any(ConfigHealthCatalog.IsSensitive)), c => Assert.Null(c.Value));
    }

    // -- paths ---------------------------------------------------------------------------------------

    [Fact]
    public void AMissingDirectory_IsAWarning_AndAConfiguredButAbsentOne_IsAnError()
    {
        var unset = Check(Report(Build()), "wiki-path");
        Assert.Equal(ConfigHealthStatus.Warning, unset.Status);
        Assert.Equal("Not set", unset.Source);
        Assert.NotNull(unset.Remedy);

        string absent = Path.Combine(_root, "absent-wiki");
        var missing = Check(Report(Build(appSettings: new() { ["WikiPath"] = absent })), "wiki-path");
        Assert.Equal(ConfigHealthStatus.Error, missing.Status);
        Assert.Equal(absent, missing.Value);

        string present = MakeDirectory("wiki");
        var ok = Check(Report(Build(appSettings: new() { ["WikiPath"] = present })), "wiki-path");
        Assert.Equal(ConfigHealthStatus.Ok, ok.Status);
        Assert.Equal(present, ok.Value);
        Assert.Null(ok.Remedy);
    }

    [Fact]
    public void TheGnollHackSource_MustBeAGitRepository_WithSrcAndInclude()
    {
        string source = MakeDirectory("gnollhack");
        MakeDirectory("gnollhack", "src");
        var configuration = Build(appSettings: new() { ["SourceCodePath"] = source });

        Assert.Equal(ConfigHealthStatus.Error, Check(Report(configuration), "source-code-path").Status);

        MakeDirectory("gnollhack", "include");
        Assert.Equal(ConfigHealthStatus.Warning, Check(Report(configuration), "source-code-path").Status);

        MakeDirectory("gnollhack", ".git");
        Assert.Equal(ConfigHealthStatus.Ok, Check(Report(configuration), "source-code-path").Status);
    }

    [Fact]
    public void TheKnowledgeBase_NeedsAContentFolder()
    {
        string kb = MakeDirectory("kb");
        var configuration = Build(appSettings: new() { ["KbPath"] = kb });

        Assert.Equal(ConfigHealthStatus.Error, Check(Report(configuration), "kb-path").Status);

        MakeDirectory("kb", "Content");
        Assert.Equal(ConfigHealthStatus.Ok, Check(Report(configuration), "kb-path").Status);
    }

    [Theory]
    [InlineData(null, ConfigHealthStatus.Info)]
    [InlineData("900", ConfigHealthStatus.Ok)]
    [InlineData("0", ConfigHealthStatus.Error)]
    [InlineData("-5", ConfigHealthStatus.Error)]
    [InlineData("big", ConfigHealthStatus.Error)]
    public void FileSizeLimits_ParseAsPositiveWholeNumbers(string? value, string expected)
    {
        var configuration = Build(memory: new() { ["MaxSourceFileSizeKB"] = value });

        Assert.Equal(expected, Check(Report(configuration), "max-source-file-size").Status);
    }

    // -- limits --------------------------------------------------------------------------------------

    [Fact]
    public void Limits_RequireMinDefaultAndMaxInOrder()
    {
        var configuration = Build(memory: new()
        {
            ["AiPerformanceSettings:IncludeToolCallHistoryDigest"] = "true",
            ["AiPerformanceSettings:MaxResultLength:Min"] = "100",
            ["AiPerformanceSettings:MaxResultLength:Default"] = "70",
            ["AiPerformanceSettings:MaxResultLength:Max"] = "50",
            ["AiPerformanceSettings:MaxToolIterations:Min"] = "3",
            ["AiPerformanceSettings:MaxToolIterations:Default"] = "22",
            ["AiPerformanceSettings:MaxToolIterations:Max"] = "50",
            ["AiPerformanceSettings:MaxCallsPerSession:Min"] = "few",
            ["AiPerformanceSettings:MaxCallsPerSession:Default"] = "150",
            ["AiPerformanceSettings:MaxCallsPerSession:Max"] = "500",
            ["AiPerformanceSettings:ChatRequestTimeout:Min"] = "5",
            ["AiPerformanceSettings:ChatRequestTimeout:Default"] = "1800"
        });

        var report = Report(configuration);

        var reversed = Check(report, "limits:MaxResultLength");
        Assert.Equal(ConfigHealthStatus.Error, reversed.Status);
        Assert.Equal(ConfigHealthCatalog.Limits, reversed.Category);
        Assert.Equal(
            new[] { "AiPerformanceSettings:MaxResultLength:Min", "AiPerformanceSettings:MaxResultLength:Default", "AiPerformanceSettings:MaxResultLength:Max" },
            reversed.Keys.ToArray());

        Assert.Equal(ConfigHealthStatus.Ok, Check(report, "limits:MaxToolIterations").Status);
        Assert.Equal(ConfigHealthStatus.Error, Check(report, "limits:MaxCallsPerSession").Status);
        Assert.Equal(ConfigHealthStatus.Warning, Check(report, "limits:ChatRequestTimeout").Status);
        Assert.DoesNotContain(report.Checks, c => c.Id == "limits:IncludeToolCallHistoryDigest");
    }

    // -- makedefs ------------------------------------------------------------------------------------

    [Fact]
    public void TheMakedefsBuildProgram_MustExist()
    {
        string msbuild = MakeFile("Program Files", "MSBuild", "MSBuild.exe");
        string absent = Path.Combine(_root, "Program Files", "Missing", "MSBuild.exe");

        var ok = Check(Report(Build(appSettings: new() { ["MakedefsBuildCommand"] = $"\"{msbuild}\" makedefs.vcxproj -nologo" })),
            "makedefs-build-command");
        Assert.Equal(ConfigHealthStatus.Ok, ok.Status);
        Assert.Contains(msbuild, ok.Summary, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(ConfigHealthStatus.Error,
            Check(Report(Build(appSettings: new() { ["MakedefsBuildCommand"] = $"\"{absent}\" makedefs.vcxproj" })), "makedefs-build-command").Status);

        Assert.Equal(ConfigHealthStatus.Info,
            Check(Report(Build(appSettings: new() { ["MakedefsBuildCommand"] = "msbuild makedefs.vcxproj" })), "makedefs-build-command").Status);

        Assert.Equal(ConfigHealthStatus.Warning, Check(Report(Build()), "makedefs-build-command").Status);
    }

    [Fact]
    public void ARelativeMakedefsBuildProgram_IsResolvedAgainstSourceCodePath()
    {
        string source = MakeDirectory("gnollhack");
        MakeFile("gnollhack", "tools", "build.cmd");

        var check = Check(Report(Build(appSettings: new()
        {
            ["SourceCodePath"] = source,
            ["MakedefsBuildCommand"] = Path.Combine("tools", "build.cmd") + " release"
        })), "makedefs-build-command");

        Assert.Equal(ConfigHealthStatus.Ok, check.Status);
    }

    [Theory]
    [InlineData(MakedefsOutcomeKind.BuildFailed, ConfigHealthStatus.Error)]
    [InlineData(MakedefsOutcomeKind.RunFailed, ConfigHealthStatus.Error)]
    [InlineData(MakedefsOutcomeKind.ExecutableMissing, ConfigHealthStatus.Warning)]
    [InlineData(MakedefsOutcomeKind.SkippedBranch, ConfigHealthStatus.Info)]
    [InlineData(MakedefsOutcomeKind.UpToDate, ConfigHealthStatus.Ok)]
    [InlineData(MakedefsOutcomeKind.Generated, ConfigHealthStatus.Ok)]
    public void TheLastMakedefsOutcome_SetsTheCheckStatus(MakedefsOutcomeKind kind, string expected)
    {
        var state = new MakedefsHealthState(kind, "output tail", DateTime.UtcNow, Array.Empty<string>());

        var check = Check(new MakedefsStateHealthService(Build(), state).GetHealthReport(), "makedefs-last-outcome");

        Assert.Equal(expected, check.Status);
        Assert.Equal(kind.ToString(), check.Value);
        Assert.Null(check.Source);
    }

    [Fact]
    public void TheLastMakedefsOutcome_NamesTheMissingHeaders()
    {
        var state = new MakedefsHealthState(MakedefsOutcomeKind.BuildFailed, null, DateTime.UtcNow, new[] { "pm.h", "onames.h" });

        var check = Check(new MakedefsStateHealthService(Build(), state).GetHealthReport(), "makedefs-last-outcome");

        Assert.Contains("pm.h, onames.h", check.Summary, StringComparison.Ordinal);
        Assert.Equal(ConfigHealthStatus.Info,
            Check(new MakedefsStateHealthService(Build(), null).GetHealthReport(), "makedefs-last-outcome").Status);
    }

    // -- the report as a whole -----------------------------------------------------------------------

    [Fact]
    public void TheReport_CountsStatusesAndNamesTheEnvironment()
    {
        var development = Report(Build(), new TestEnvironment());
        Assert.Equal("Development", development.EnvironmentName);
        Assert.True(development.UserSecretsExpected);

        var production = Report(Build(), new TestEnvironment { EnvironmentName = Environments.Production });
        Assert.Equal("Production", production.EnvironmentName);
        Assert.False(production.UserSecretsExpected);

        var none = Report(Build());
        Assert.Null(none.EnvironmentName);
        Assert.False(none.UserSecretsExpected);

        var statuses = new[] { ConfigHealthStatus.Ok, ConfigHealthStatus.Warning, ConfigHealthStatus.Error, ConfigHealthStatus.Info };
        Assert.All(development.Checks, c => Assert.Contains(c.Status, statuses));
        Assert.All(development.Checks, c => Assert.Equal(c.Status == ConfigHealthStatus.Ok, c.Remedy == null));
        Assert.Equal(development.Checks.Count, development.Summary.Ok + development.Summary.Warning + development.Summary.Error + development.Summary.Info);
        Assert.Equal(development.Checks.Count(c => c.Status == ConfigHealthStatus.Error), development.Summary.Error);
        Assert.Equal(development.Checks.Count, development.Checks.Select(c => c.Id).Distinct().Count());
    }
}
