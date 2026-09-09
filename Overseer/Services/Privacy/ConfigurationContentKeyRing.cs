using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace Overseer.Services.Privacy;

/// <summary>
/// The keyring backed by configuration: <c>PrivacySettings:KeyRing:v1 … vN</c> and
/// <c>PrivacySettings:ActiveKeyVersion</c>.
/// </summary>
/// <remarks>
/// <para>
/// **Key material belongs in User Secrets, never <c>appsettings.json</c>** — this repository's
/// configuration-management convention, and the reason `appsettings.json` carries only the
/// section's shape with no values.
/// </para>
/// <para>
/// A missing or malformed ring is **not** an exception here. It is reported through
/// <c>ConfigHealthService</c> as a startup alert, because throwing from the constructor would
/// take down an application whose non-confidential functionality is entirely fine, and
/// throwing lazily would surface the misconfiguration on a user's first confidential turn —
/// the worst possible moment to discover it. <see cref="IsUsable"/> is what the encryptor
/// checks, and <see cref="ValidationError"/> is what the alert reports.
/// </para>
/// </remarks>
public class ConfigurationContentKeyRing : IContentKeyRing
{
    private const int RequiredKeyBytes = 32;

    private readonly Dictionary<string, byte[]> _keys = new(StringComparer.OrdinalIgnoreCase);

    public ConfigurationContentKeyRing(IConfiguration configuration)
    {
        var section = configuration.GetSection("PrivacySettings:KeyRing");
        var malformed = new List<string>();

        foreach (var child in section.GetChildren())
        {
            if (string.IsNullOrWhiteSpace(child.Value))
            {
                malformed.Add($"{child.Key} is empty");
                continue;
            }

            byte[] key;
            try
            {
                key = Convert.FromBase64String(child.Value.Trim());
            }
            catch (FormatException)
            {
                malformed.Add($"{child.Key} is not valid base64");
                continue;
            }

            /* The same check CryptoService applies to AesEncryptionKey. A short key would be
               accepted by nothing downstream, and a long one would silently truncate. */
            if (key.Length != RequiredKeyBytes)
            {
                malformed.Add($"{child.Key} is {key.Length} bytes, not {RequiredKeyBytes}");
                continue;
            }

            _keys[child.Key] = key;
        }

        ActiveVersion = configuration["PrivacySettings:ActiveKeyVersion"]?.Trim() ?? string.Empty;

        ValidationError = DescribeProblem(malformed);
    }

    public string ActiveVersion { get; }

    public IReadOnlyCollection<string> Versions => _keys.Keys.ToList();

    /// <summary>
    /// Whether the ring can actually be used to encrypt. False means confidential sessions
    /// cannot store encrypted content, and <see cref="ValidationError"/> says why.
    /// </summary>
    public bool IsUsable => ValidationError == null;

    /// <summary>The reason the ring is unusable, phrased for an administrator. Null when it is fine.</summary>
    public string? ValidationError { get; }

    public byte[] GetKey(string version)
    {
        if (!_keys.TryGetValue(version ?? string.Empty, out var key))
        {
            throw new KeyNotFoundException(
                $"Content key version '{version}' is not in PrivacySettings:KeyRing. A session encrypted "
                + "under it cannot be read until that version is restored.");
        }

        return key;
    }

    public bool HasVersion(string version) => _keys.ContainsKey(version ?? string.Empty);

    private string? DescribeProblem(List<string> malformed)
    {
        if (_keys.Count == 0 && malformed.Count == 0)
        {
            return "No content keys are configured. Set PrivacySettings:KeyRing:v1 to a base64 32-byte key "
                + "in User Secrets, and PrivacySettings:ActiveKeyVersion to v1.";
        }

        if (malformed.Count > 0)
        {
            return "One or more content keys are malformed: " + string.Join("; ", malformed)
                + ". Each must be a base64-encoded 32-byte key.";
        }

        if (string.IsNullOrEmpty(ActiveVersion))
        {
            return "PrivacySettings:ActiveKeyVersion is not set. It must name one of the configured key "
                + "versions: " + string.Join(", ", _keys.Keys) + ".";
        }

        if (!_keys.ContainsKey(ActiveVersion))
        {
            /* The subtlest of the four, and the one most likely to happen during a rotation:
               the ring is well-formed and the active pointer names a version nobody added. */
            return $"PrivacySettings:ActiveKeyVersion is '{ActiveVersion}', which is not in the keyring. "
                + "Configured versions: " + string.Join(", ", _keys.Keys) + ".";
        }

        return null;
    }
}
