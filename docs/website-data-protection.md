# Data Protection Key Ring for the Website (MobileGnollHackLogger)

The website protects its cookies and emailed tokens with ASP.NET Core Data Protection. The keys it uses form a **key ring**, which `Program.cs` persists to a configured folder under a fixed application name. Overseer has its own, separate ring; see `Overseer/Program.cs` and `PrivacySettings:DataProtectionKeysPath` in Overseer's configuration.

---

## 1. What the Key Ring Protects

- Sign-in cookies and "remember me" cookies.
- Email confirmation and password reset links (Identity's `DataProtectorTokenProvider`).
- Two-factor "remember this browser" cookies and TempData cookies.
- Antiforgery tokens, in the antiforgery cookie and in every rendered form.

**Losing the ring invalidates all of them at once.** Every signed-in user is silently signed out, every outstanding confirmation and reset link fails with an invalid-token error, and a POST from a form that was open before the loss answers 400. A GET with an unreadable antiforgery cookie is harmless: the cookie is discarded and a new one issued.

## 2. The Setting

`PrivacySettings:DataProtectionKeysPath` names the ring's folder.

- **An absolute path outside the site folder.** A publish that cleans the target folder would otherwise delete the ring.
- **Created at startup** if it does not exist.
- **Set in the host configuration** (`applicationHost.config`, or wherever `SentryDSN` is set), which a publish does not overwrite. `MobileGnollHackLogger/appsettings.json` carries the key with an empty value; never leave a server on that empty value, and never put the path in the published `web.config` or `appsettings.json`.
- **One folder per site.** Production and staging never share a folder.

## 3. Permissions and Custody

- The application pool identity needs **Modify** on the folder.
- **The key files are not encrypted** (no DPAPI, as in Overseer). Whoever reads them can forge a sign-in cookie for any account of the website, so the folder's ACL is their protection. Remove the inherited entries and keep three: Administrators and SYSTEM with full control, and the site's own application pool with Modify. On a default Windows volume the inherited entries let every local account, and every other application pool on the server, read the folder.
- **A backup of the folder holds the keys too**, and needs the same care.
- The framework logs a Warning, *"No XML encryptor configured"*, each time it writes a new key (about every 90 days). With this setup that is expected.

## 4. The Application Name

`Program.cs` calls `SetApplicationName("GnollHackAccount")`.

- It differs from Overseer's `GnollHackOverseer`, so the two sites keep separate rings even if both folders were ever pointed at the same place.
- The antiforgery cookie's name, `.AspNetCore.Antiforgery.<suffix>`, ends in a hash derived from the application name. **Changing the name renames that cookie** and makes everything protected under the old name unreadable, even with the same ring, which signs every user out once.

## 5. When the Setting Is Unset

- The framework default applies: the application pool's user profile (`%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`), else the HKLM registry, else **memory only**, lost on every restart and application pool recycle. Which one applies depends on the pool's name, identity and *Load User Profile* setting, not on the code.
- In the **Staging** and **Production** environments the website logs at Error once per start, which reaches Sentry:

  > PrivacySettings:DataProtectionKeysPath is not set. Data Protection keys fall back to the framework default location and can be lost on a restart, which signs every user out.

  Development and Testing log nothing.

## 6. Recognizing a Lost Ring in Sentry

- `AntiforgeryValidationException: The antiforgery token could not be decrypted.` with an inner `CryptographicException: The key {…} was not found in the key ring.`, shortly after a start: the ring the process loaded does not hold the key the cookie was protected with. Sentry issue ACCOUNT-7 was this.
- *"The payload was invalid"* instead means the ring is there and the application name changed.

## 7. Checking the Server

- The configured folder should hold one or more `key-*.xml` files, and a restart or recycle should not add a new one.
- Without a configured folder, look for the ring in the pool's profile, `C:\Users\<pool name>\AppData\Local\ASP.NET\DataProtection-Keys`, and under `HKLM\SOFTWARE\Microsoft\ASP.NET\4.0.30319.0\AutoGenKeys\<pool SID>\DataProtection`. When the ring is in memory only, Data Protection logs a Warning at startup saying so, which reaches the Windows Application event log.
