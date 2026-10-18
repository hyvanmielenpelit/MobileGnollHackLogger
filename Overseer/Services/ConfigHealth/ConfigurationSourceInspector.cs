using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;

namespace Overseer.Services.ConfigHealth;

/// <summary>
/// Says which configuration provider supplies a key and lists the loaded providers. It reads values only
/// to test them for emptiness and never returns one.
/// </summary>
public sealed class ConfigurationSourceInspector
{
    public const string NotSet = "Not set";
    public const string UserSecrets = "User Secrets";
    public const string EnvironmentVariables = "Environment variables";
    public const string CommandLine = "Command line";

    /// <summary>Reported for a set key when the configuration exposes no providers.</summary>
    public const string Unknown = "Unknown";

    private const string UserSecretsFileName = "secrets.json";

    private readonly IConfiguration _configuration;
    private readonly IReadOnlyList<IConfigurationProvider> _providers;

    public ConfigurationSourceInspector(IConfiguration configuration)
    {
        _configuration = configuration;
        _providers = configuration is IConfigurationRoot root
            ? Flatten(root.Providers).ToList()
            : Array.Empty<IConfigurationProvider>();
    }

    /// <summary>The providers in load order, a chained configuration's own providers in its place.</summary>
    public IReadOnlyList<IConfigurationProvider> Providers => _providers;

    /// <summary>
    /// The provider whose value wins for <paramref name="key"/>: the last one that has the key or, for a
    /// section, any key under it. Null when none has it.
    /// </summary>
    public IConfigurationProvider? FindProvider(string key)
    {
        for (int i = _providers.Count - 1; i >= 0; i--)
        {
            if (Has(_providers[i], key))
            {
                return _providers[i];
            }
        }

        return null;
    }

    /// <summary>The name of the provider that supplies <paramref name="key"/>, or <see cref="NotSet"/>.</summary>
    public string Describe(string key)
    {
        var provider = FindProvider(key);
        if (provider != null)
        {
            return NameOf(provider);
        }

        if (_providers.Count == 0 && _configuration.GetSection(key).Exists())
        {
            return Unknown;
        }

        return NotSet;
    }

    /// <summary>
    /// The names of the <c>appsettings*.json</c> files that hold a non-empty value for <paramref name="key"/>
    /// or for any key under it, whichever provider wins. A value equal to <paramref name="ignoredValue"/> does not count.
    /// </summary>
    public IReadOnlyList<string> AppSettingsFilesWithValue(string key, string? ignoredValue = null)
        => _providers
            .Where(p => KindOf(p) == "appsettings" && HasNonEmptyValue(p, key)
                && !(ignoredValue != null && p.TryGet(key, out string? value) && string.Equals(value, ignoredValue, StringComparison.Ordinal)))
            .Select(NameOf)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The loaded providers with their key counts, in load order.</summary>
    public IReadOnlyList<ConfigSourceDto> ListSources()
        => _providers.Select(p => new ConfigSourceDto
        {
            Name = NameOf(p),
            Kind = KindOf(p),
            FileExists = FileExists(p),
            KeyCount = CountKeys(p)
        }).ToList();

    public static string NameOf(IConfigurationProvider provider)
    {
        if (provider is FileConfigurationProvider file)
        {
            string fileName = FileName(file);
            if (string.Equals(fileName, UserSecretsFileName, StringComparison.OrdinalIgnoreCase))
            {
                return UserSecrets;
            }

            return string.IsNullOrEmpty(fileName) ? provider.GetType().Name : fileName;
        }

        return provider switch
        {
            EnvironmentVariablesConfigurationProvider => EnvironmentVariables,
            CommandLineConfigurationProvider => CommandLine,
            _ => provider.GetType().Name
        };
    }

    public static string KindOf(IConfigurationProvider provider)
    {
        if (provider is FileConfigurationProvider file)
        {
            string fileName = FileName(file);
            if (string.Equals(fileName, UserSecretsFileName, StringComparison.OrdinalIgnoreCase))
            {
                return "userSecrets";
            }

            if (fileName.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
                && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return "appsettings";
            }

            return "jsonFile";
        }

        return provider switch
        {
            EnvironmentVariablesConfigurationProvider => "environmentVariables",
            CommandLineConfigurationProvider => "commandLine",
            MemoryConfigurationProvider => "memory",
            _ => "other"
        };
    }

    private static string FileName(FileConfigurationProvider provider)
        => string.IsNullOrEmpty(provider.Source.Path) ? string.Empty : Path.GetFileName(provider.Source.Path);

    private static bool? FileExists(IConfigurationProvider provider)
    {
        if (provider is not FileConfigurationProvider file || string.IsNullOrEmpty(file.Source.Path))
        {
            return null;
        }

        try
        {
            return file.Source.FileProvider != null
                ? file.Source.FileProvider.GetFileInfo(file.Source.Path).Exists
                : File.Exists(file.Source.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static IEnumerable<IConfigurationProvider> Flatten(IEnumerable<IConfigurationProvider> providers)
    {
        foreach (var provider in providers)
        {
            if (provider is ChainedConfigurationProvider chained && chained.Configuration is IConfigurationRoot inner)
            {
                foreach (var nested in Flatten(inner.Providers))
                {
                    yield return nested;
                }
            }
            else
            {
                yield return provider;
            }
        }
    }

    private static IEnumerable<string> ChildPaths(IConfigurationProvider provider, string? parentPath)
        => provider.GetChildKeys(Enumerable.Empty<string>(), parentPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(child => parentPath == null ? child : ConfigurationPath.Combine(parentPath, child));

    private static bool Has(IConfigurationProvider provider, string key)
        => provider.TryGet(key, out _) || ChildPaths(provider, key).Any();

    private static bool HasNonEmptyValue(IConfigurationProvider provider, string key)
    {
        if (provider.TryGet(key, out string? value) && !string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return ChildPaths(provider, key).Any(child => HasNonEmptyValue(provider, child));
    }

    private static int CountKeys(IConfigurationProvider provider)
    {
        try
        {
            int count = 0;
            var pending = new Stack<string?>();
            pending.Push(null);
            while (pending.Count > 0)
            {
                foreach (string child in ChildPaths(provider, pending.Pop()))
                {
                    if (provider.TryGet(child, out _))
                    {
                        count++;
                    }

                    pending.Push(child);
                }
            }

            return count;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return 0;
        }
    }
}
