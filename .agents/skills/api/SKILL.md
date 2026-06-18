---
name: MobileGnollHackLogger API Endpoints
description: Complete reference for all Web API controllers and endpoints in the MobileGnollHackLogger project. Covers all routes, HTTP methods, request models, authentication flow, business logic, and response formats for the LogController (xlogfile), BonesController, DumpLogController, JunetHackController, SaveFileTrackingController, and ReplayController.
---

# API Endpoints Reference

All controllers are in `MobileGnollHackLogger/Areas/API/` under namespace `MobileGnollHackLogger.Areas.API`.

## Authentication Pattern

All POST endpoints use the same per-request authentication sequence:

1. Validate `AntiForgeryToken` against `_configuration["AntiForgeryToken"]` → **401** if wrong
2. `SignInManager.PasswordSignInAsync(UserName, Password)` → **403** if failed, **412** if 2FA required, **423** if locked out
3. Check `user.IsBanned` → **423**; check feature-specific bans (`IsGameLogBanned`, `IsBonesBanned`) → **423**
4. Process business logic

This is **NOT** token-based or cookie-based auth. Credentials are sent with every API call via form data.

### LoginInfoModel (base class for all authenticated models)

```csharp
// Areas/API/LoginInfoModel.cs
string? UserName        // [Required, RegularExpression(@"^[A-Za-z0-9][A-Za-z0-9_]{0,30}$")]
string? Password        // [Required, MaxLength(63)]
string? AntiForgeryToken  // [Required]
```

---

## LogController — xlogfile & Game Log Submission

File: `Areas/API/LogController.cs` — `[ApiController]`, no `[Route]` on class

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/xlogfile` | None | All game logs in xlog format; supports HTTP Range header |
| GET | `/xlogfile/{id}` | None | Single game log entry by DB ID |
| GET | `/xlogfile/min/{minId}` | None | Game logs with ID ≥ minId |
| GET | `/xlogfile/max/{maxId}` | None | Game logs with ID ≤ maxId |
| GET | `/xlogfile/minmax/{minId}/{maxId}` | None | Game logs in ID range |
| POST | `/xlogfile` | Yes | Submit new game log with dump logs |

**Dependencies:** `SignInManager`, `UserManager`, `IConfiguration`, `ApplicationDbContext`. Reads `DumpLogPath` from configuration. Uses `DbLogger` with `LogType.GameLog`.

### GET `/xlogfile` — Range Request Support

- Parses `Range: bytes=min-max` header
- Uses `GameLog.ByteStart`/`ByteEnd` fields for efficient seeking when the requested range falls within a known entry
- Falls back to scanning from the last known entry or from the beginning
- Lazily populates `ByteStart`/`ByteEnd`/`ByteLength` on GameLog entries that lack them, saving to DB
- Outputs ASCII-encoded xlog lines with Unix newlines (`\n`)
- Writes directly to `Response.BodyWriter` for streaming (not buffered)
- Returns 400 for malformed Range headers, 416 for invalid ranges

### POST `/xlogfile` — Submit Game Log

**Model:** `LogModel` (extends `LoginInfoModel`)
```csharp
// Areas/API/LogModel.cs
IFormFile? PlainTextDumpLog
IFormFile? HtmlDumpLog
string? XLogEntry
```

**Business logic (3 modes):**

1. **Full submission** (all three fields present):
   - Parses `XLogEntry` into `XLogFileLine`
   - Overrides `Name` with authenticated `UserName` (prevents impersonation)
   - Saves dump log files: `{DumpLogPath}/{UserName}/gnollhack.{UserName}.{StartTimeUTC}.{txt|html}`
   - Checks for duplicate dump log files → **409**
   - Creates `GameLog` DB entry via `new GameLog(xLogFileLine, dbContext)`
   - Computes top score ranking via `GetTopScoreNumberAsync`
   - Returns JSON `LogPostResponseInfo` as `text/plain`

2. **Test connection** (all three fields empty): returns **200 OK**

3. **Partial data** (mixed): returns **400**

**Response model:**
```csharp
// Areas/API/LogPostResponseInfo.cs
long DatabaseRowId
long TopScoreDisplayIndex
long TopScoreIndex
string? TopScorePageUrl
```

---

## DumpLogController — Character Dump Log Retrieval

File: `Areas/API/DumpLogController.cs` — `[Route("dumplog")]`, `[ApiController]`

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/dumplog/{id:int}` | None | Dump log by game log DB ID (default HTML) |
| GET | `/dumplog/{id:int}/{type}` | None | Dump log with type (html or plain) |
| GET | `/dumplog/byname/{name}/{starttime}` | None | Dump log by player name + start time (HTML) |
| GET | `/dumplog/byname/{name}/{starttime}/{type}` | None | Same with type |

**Dependencies:** `IConfiguration`, `ApplicationDbContext`. Reads `DumpLogPath`.

**Logic:** Looks up `GameLog` by ID or by `Name`+`StartTime`. Reads file from `{DumpLogPath}/{Name}/gnollhack.{Name}.{StartTimeUTC}.{ext}`. Returns 404 if game log not found, 410 if file doesn't exist on disk.

---

## BonesController — Bones File Exchange

File: `Areas/API/BonesController.cs` — `[ApiController]`, no `[Route]` on class

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/bones` | None | Returns bones entry 0 as text |
| GET | `/bones/{id}` | None | Returns bones entry by ID as text |
| POST | `/bones` | Yes | Bones file exchange |

**Dependencies:** `SignInManager`, `UserManager`, `IConfiguration`, `ApplicationDbContext`. Reads `BonesPath`. Uses `DbLogger` with `LogType.Bones`.

**Model:** `BonesModel` (standalone, not extending `LoginInfoModel` but has same auth fields)
```csharp
// Areas/API/BonesModel.cs
IFormFile? BonesFile      // [JsonIgnore]
string? Command           // "SendBonesFile" or "ConfirmReceipt"
string? Data              // Difficulty level (SendBonesFile) or file path (ConfirmReceipt)
string? AllowedUsers      // [MaxLength(2048), RegularExpression] — whitelist or blacklist (prefix with !)
ulong VersionNumber
ulong VersionCompatibilityNumber
string? Platform, PlatformVersion, Port, PortVersion, PortBuild
string? UserName          // [Required, MaxLength(31), JsonIgnore]
string? Password          // [Required, MaxLength(63), JsonIgnore]
string? AntiForgeryToken  // [Required, JsonIgnore]
```

### Command: "SendBonesFile"

1. Parses difficulty from `Data` field
2. Counts existing bones (server limit: 512 total, 32 per user) — skips upload if exceeded
3. Stores file to `{BonesPath}/{UserName}/{FileName}_{index}` (index incremented until unique)
4. Creates `Bones` DB entry + `BonesTransaction(Upload)`
5. Queries for eligible bones to return:
   - From other non-banned users
   - Same difficulty level
   - Version compatible (bidirectional `VersionNumber`/`VersionCompatibilityNumber` check)
   - Respects `AllowedUsers` whitelist/blacklist
6. Probability throttling: min 4 needed, 50-100% chance between 4-128 available (linear scale)
7. Randomly selects one, reads file bytes, returns as `application/octet-stream`
8. Custom response headers: `X-GH-OriginalFileName`, `X-GH-BonesFilePath`, `X-GH-DataBaseTableId`
9. Creates `BonesTransaction(Download)` on successful return

### Command: "ConfirmReceipt"

1. `Data` field contains the server bones file path (from `X-GH-BonesFilePath`)
2. Finds `Bones` entry by `BonesFilePath == model.Data`
3. Removes DB entry, deletes physical file
4. Creates `BonesTransaction(Deletion)`
5. Returns 200 if file deleted, 410 if file didn't exist

### Test Connection

Empty `Data` + no `BonesFile` → returns **200 OK**

---

## JunetHackController — JunetHack Username Lookup

File: `Areas/API/JunetHackController.cs` — `[ApiController]`

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/junethack/{username}` | None | Returns user's JunetHack username |

**Dependencies:** `UserManager`.

Returns `"junethack {JunetHackUserName}"` as `text/plain` (ASCII), empty string if not set, **404** if user not found.

---

## SaveFileTrackingController — Anti-Cheat Save File Tracking

File: `Areas/API/SaveFileTrackingController.cs` — `[Route("api/[controller]")]`, `[ApiController]`

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/api/SaveFileTracking/create` | Yes | Create save file tracking entry |
| POST | `/api/SaveFileTracking/use` | Yes | Mark save file as loaded |

**Dependencies:** `SignInManager`, `UserManager`, `IConfiguration`, `ApplicationDbContext`. Uses `DbLogger` with `LogType.SaveFileTracking`.

### Create Endpoint

**Model:** `SaveFileTrackingCreateModel` (extends `LoginInfoModel`)
```csharp
long TimeStamp     // [Required]
string? Sha256     // [Required] — base64-encoded, must decode to 32 bytes
long FileLength    // [Required] — must be > 0
```

**Logic:**
1. Checks for duplicate `TimeStamp + AspNetUserId` → **409**
2. Validates SHA256 format and FileLength
3. Creates `SaveFileTracking` DB entry
4. Encrypts the DB ID using AES-256-CBC, returns base64-encoded encrypted ID

### Use Endpoint

**Model:** `SaveFileTrackingUseModel` (extends `LoginInfoModel`)
```csharp
string? EncryptedId  // [Required] — encrypted DB ID from Create response
long TimeStamp       // [Required]
string? Sha256       // [Required]
long FileLength      // [Required]
```

**Logic:**
1. Decrypts `EncryptedId` to recover DB ID
2. Cross-validates against DB record: TimeStamp, Sha256, FileLength, UserName must all match
3. Checks `UsedDate` is null (not already used) → **409**
4. Sets `UsedDate = DateTime.UtcNow`

**Encryption:** AES-256-CBC, key from `EncryptionKeyString` (32 bytes UTF-8), IV from `EncryptionIVString` (16 bytes UTF-8), PKCS7 padding. The encrypted value is base64-encoded for transport.

---

## ReplayController (Stub)

File: `Areas/API/ReplayController.cs` — `[Route("api/replay")]`, `[ApiController]`

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/api/replay` | Yes | Placeholder — auth works, no business logic yet |

**Model:** `ReplayModel` (standalone, same auth fields as `LoginInfoModel` with slightly different UserName regex: `^[A-Za-z0-9_]{1,31}$`)
