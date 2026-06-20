---
name: MobileGnollHackLogger Data Models
description: Reference for Entity Framework Core data models (ApplicationUser, GameLog, Bones, SaveFileTracking, RequestInfo), XLogFileLine schema, enums, and helpers (DbLogger).
---

# Data Models & Database Schema (`Data/`)

## 🗄️ ApplicationDbContext
*   **DbSets:** `GameLog`, `Bones`, `RequestLogs` (`RequestInfo`), `BonesTransactions`, `SaveFileTrackings`.
*   **EF Defaults:** Date columns (like `Created`, `FirstDate`) default to `getutcdate()` via `OnModelCreating`.
*   **Method `GetTopScoreNumberAsync`:** Computes top score ranking (handling ties) based on Points DESC for `Scoring == "yes"`.

## 🧬 Key Entities

### ApplicationUser (IdentityUser)
*   **Relationships:** 1:N to `GameLog`, `Bones`, `BonesTransaction`, `RequestInfo`, `SaveFileTracking`.
*   **Fields:** Includes ban flags (`IsBanned`, `IsGameLogBanned`, `IsBonesBanned`) and `JunetHackUserName`.

### XLogFileLine & GameLog
*   **XLogFileLine:** Base class. Parses a tab-separated xlogfile line into 47+ typed properties. Contains output logic (`ToString(OutputMode.XLog)`). Security properties (like `seclvl`, `xplvl`) are *omitted* if null to preserve byte ranges for old entries.
*   **GameLog:** Mapped to DB table. Inherits `XLogFileLine`. Adds PK (`Id`), FK (`AspNetUserId`), and `ByteStart`/`ByteEnd`/`ByteLength` fields used for fast seeking when responding to HTTP Range requests.

### Bones & BonesTransaction
*   **Bones:** Represents an uploaded bones file. Fields include `DifficultyLevel`, `BonesFilePath`, and `VersionNumber`/`VersionCompatibilityNumber`.
*   **BonesTransaction:** Logs bones exchanges. `Type` enum: Upload (0), Download (1), Deletion (2).

### SaveFileTracking
*   **Anti-Cheat:** Used to prevent save scumming. Tracks `TimeStamp`, `Sha256`, and `FileLength`.
*   **Constraints:** Unique composite index on `[TimeStamp, AspNetUserId]`. `UsedDate` is set when the save is loaded.

### RequestInfo (Logging)
*   **Deduplication:** The `DbLogger` helper writes to this table. If a request is identical (same path, method, data, user, etc. but ignoring IP), it *increments* `Count` and updates `LastDate` instead of creating a new row.

## 🛠️ Helpers

*   **DbLogger:** Core logging service. Deduplicates logs. Call `LogRequestAsync(message, level, statusCode)`.
*   **GnollHackHelper:** Static dictionary maps for Roles, Difficulties, Modes, Races.
*   **LogFileLogger:** Appends to a local text file. Uses `DateTime.Now` (local time, not UTC).
*   **EmailSender:** Azure Communication Services wrapper. Connects via `DoNotReply@gnollhack.com`.
