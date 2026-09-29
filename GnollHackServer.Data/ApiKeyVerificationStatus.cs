namespace MobileGnollHackLogger.Data;

/// <summary>
/// Outcome of the last check of a stored API key with its provider. A null column means the key
/// has never been checked.
/// </summary>
public enum ApiKeyVerificationStatus
{
    Verified = 1,
    NotVerified = 2,
}
