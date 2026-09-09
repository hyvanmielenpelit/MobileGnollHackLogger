using System;
using System.Collections.Generic;

namespace Overseer.Services.Privacy;

/// <summary>
/// The versioned master keys that wrap per-session content keys.
/// </summary>
/// <remarks>
/// <para>
/// An interface so a Key Vault or HSM implementation can replace the configuration one without
/// touching a call site or the schema: the session row records only a version string, and
/// resolving that string to bytes is entirely this type's business.
/// </para>
/// <para>
/// **This is a second master key, and it is additive to the first.** <c>CryptoService</c> reads
/// an unversioned <c>AesEncryptionKey</c> for API-key material and keeps doing so; that key does
/// not rotate. <c>KeyRing:v1</c> may be seeded from the same value so a deployment starts with
/// one secret, but the two paths stay independent — the keyring rotates and
/// <c>AesEncryptionKey</c> does not. Folding the API-key path onto the keyring is a reasonable
/// future change and is deliberately out of scope: it needs its own migration and its own
/// re-wrap pass.
/// </para>
/// </remarks>
public interface IContentKeyRing
{
    /// <summary>
    /// The version new content keys are wrapped under. Every version in
    /// <see cref="Versions"/> stays available for unwrapping.
    /// </summary>
    string ActiveVersion { get; }

    /// <summary>Every version the ring can unwrap with, active and retired alike.</summary>
    IReadOnlyCollection<string> Versions { get; }

    /// <summary>
    /// The 32-byte key for a version.
    /// </summary>
    /// <exception cref="KeyNotFoundException">
    /// The version is not in the ring — which for a stored session means its content is
    /// unreadable until that version is restored. Retiring a version while any session still
    /// references it is the one rotation mistake that loses data.
    /// </exception>
    byte[] GetKey(string version);

    /// <summary>Whether a version is present, for a health check that must not throw.</summary>
    bool HasVersion(string version);
}
