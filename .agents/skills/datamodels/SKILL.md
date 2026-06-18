---
name: MobileGnollHackLogger Data Models
description: Complete reference for all Entity Framework Core data models in the MobileGnollHackLogger project. Covers every entity class with exact property names, types, MaxLength attributes, foreign keys, and database annotations. Includes the XLogFileLine 47-field schema, all enums, the ApplicationDbContext configuration, entity relationships, and helper classes (DbLogger, GnollHackHelper, EmailSender).
---

# Data Models & Database Schema

All entities are in `MobileGnollHackLogger/Data/` under namespace `MobileGnollHackLogger.Data`.

## ApplicationUser : IdentityUser

File: `Data/ApplicationUser.cs`

| Property | Type | Attributes | Notes |
|----------|------|------------|-------|
| GameLogs | `ICollection<GameLog>?` | — | Navigation property |
| IsBanned | `bool?` | — | General ban flag |
| IsGameLogBanned | `bool?` | — | Game log submission ban |
| IsBonesBanned | `bool?` | — | Bones exchange ban |
| JunetHackUserName | `string?` | `[MaxLength(255)]`, `[RegularExpression("^[a-zA-Z0-9_]$")]` | JunetHack tournament username |

## XLogFileLine (Base class for GameLog)

File: `Data/XLogFileLine.cs` (~900 lines)

The largest entity — represents one line in the xlogfile format. Base class for `GameLog`. **Not directly mapped to a database table** — only `GameLog` (which inherits it) has a table.

### Stored Properties

| Property | Type | MaxLength | xlogfile Key |
|----------|------|-----------|-------------|
| Version | `string?` | 32 | version |
| EditLevel | `int` | — | edit |
| Platform | `string?` | 32 | platform |
| PlatformVersion | `string?` | 32 | platformversion |
| Port | `string?` | 128 | port |
| PortVersion | `string?` | 32 | portversion |
| PortBuild | `string?` | 32 | portbuild |
| Points | `long` | — | points |
| DeathDungeonNumber | `int` | — | deathdnum |
| DeathLevel | `int` | — | deathlev |
| MaxLevel | `int` | — | maxlvl |
| HitPoints | `int` | — | hp |
| MaxHitPoints | `int` | — | maxhp |
| Deaths | `int` | — | deaths |
| DeathDateText | `string?` | 8 | deathdate |
| BirthDateText | `string?` | 8 | birthdate |
| ProcessUserID | `int` | — | uid |
| Role | `string?` | 3 | role |
| Race | `string?` | 3 | race |
| Gender | `string?` | 3 | gender |
| Alignment | `string?` | 3 | align |
| Name | `string?` | 32 | name |
| CharacterName | `string?` | 32 | cname |
| DeathText | `string?` | — | death |
| WhileText | `string?` | — | while |
| ConductsBinary | `string?` | 50 | conduct |
| Turns | `int` | — | turns |
| AchievementsBinary | `string?` | 50 | achieve |
| AchievementsText | `string?` | — | achieveX |
| ConductsText | `string?` | — | conductX |
| RealTime | `long` | — | realtime |
| StartTime | `long` | — | starttime |
| StartTimeUTC | `long` | — | starttimeUTC |
| EndTime | `long` | — | endtime |
| EndTimeUTC | `long` | — | endtimeUTC |
| StartingGender | `string?` | 3 | gender0 |
| StartingAlignment | `string?` | 3 | align0 |
| FlagsBinary | `string?` | 50 | flags |
| Difficulty | `int` | — | difficulty |
| Mode | `string?` | 32 | mode |
| Scoring | `string?` | 32 | scoring |
| Tournament | `string?` | 32 | tournament |
| DungeonCollapses | `int` | — | collapse |
| SecurityLevel | `int?` | — | seclvl |
| Store | `string?` | 32 | store |
| PortSecurityLevel | `int?` | — | portseclvl |
| ExperienceLevel | `int?` | — | xplvl |

### Computed Properties (not mapped to DB)

- `PlatformText` — android→"Android", ios→"iOS", maccatalyst→"Mac Catalyst", macos→"macOS", winui/windows→"Windows"
- `PlatformLetter` — a, i, m, w, o
- `DeathDate` / `BirthDate` — `DateTime?` parsed from text ("yyyyMMdd")
- `RoleText` — full role name via `GnollHackHelper.GetRoleText(Role, Gender)`
- `RaceText` — full race name via `GnollHackHelper.GetRaceText(Race)`
- `GenderText` — Mal→Male, Fem→Female
- `AlignmentText` — Law→Lawful, Neu→Neutral, Cha→Chaotic
- `IsAscension` — `DeathText == "ascended"`
- `IsScoring` — `Scoring == "yes"`
- `StoreText` — google→"Google Play Store", apple→"Apple App Store", microsoft→"Microsoft Store", steam→"Steam", steam-playtest→"Steam Playtest", none→"None"
- `StoreLetter` — apple→"", google→"", microsoft→"m", steam→"s", steam-playtest→"t", unpackaged→"u", packaged→"p", none→"n", unknown→"?"
- `RealTimeSpan` — `TimeSpan.FromSeconds(RealTime)`
- `StartTimeUTCDate` / `EndTimeUTCDate` — `DateTimeOffset` from unix seconds
- `StartingGenderText`, `StartingAlignmentText` — converted like Gender/Alignment
- `DifficultyText` — maps from `GnollHackHelper.Difficulties`
- `ModeText` — maps from `GnollHackHelper.Modes`
- `IsTournament` — `Tournament == "yes"`
- `TournamentText` — returns `Tournament ?? "no"`

### Output Modes & Serialization

```csharp
enum OutputMode { XLog, CSV }
```

- `ToString(OutputMode, long? id)` — Serializes all fields. For XLog: tab-separated `key=value`. For CSV: tab-separated values only. **Critical:** SecurityLevel, Store, PortSecurityLevel, ExperienceLevel are added only if non-null (to preserve byte ranges for NetHack Scoreboard and JunetHack).
- `ToXLogString()` → `ToString(OutputMode.XLog)`
- `ToCsvString()` → `ToString(OutputMode.CSV)`
- `static GetCsvHeader(bool hasIdColumn)` — CSV header row from `_headerTexts` list (47 keys)

### Constructor: `XLogFileLine(string entry)`

Parses a tab-separated xlogfile line: splits by `\t`, then splits each item by `=`, uses a switch statement mapping all 47+ field keys to properties. Special case: if key is `"id"` and `this is GameLog`, casts and sets `GameLog.Id`.

## GameLog : XLogFileLine

File: `Data/GameLog.cs` — `[PrimaryKey(nameof(Id))]`

Inherits all 47 properties from `XLogFileLine` plus:

| Property | Type | Attributes | Notes |
|----------|------|------------|-------|
| Id | `long` | `[DatabaseGenerated(Identity)]` | Auto-increment PK |
| AspNetUserId | `string?` | `[ForeignKey("AspNetUser")]` | FK to Identity user |
| AspNetUser | `ApplicationUser?` | — | Navigation property |
| CreatedDate | `DateTime?` | — | When the record was created |
| ByteStart | `long?` | — | xlogfile byte offset start |
| ByteEnd | `long?` | — | xlogfile byte offset end |
| ByteLength | `long?` | — | xlogfile entry byte length |

**Constructors:**
- `GameLog()` — parameterless
- `GameLog(string entry)` — delegates to `XLogFileLine(entry)` parser
- `GameLog(XLogFileLine line, ApplicationDbContext dbContext)` — copies all 47 properties from XLogFileLine, looks up user by `line.Name`

## Bones

File: `Data/Bones.cs` — `[PrimaryKey(nameof(Id))]`

| Property | Type | Attributes | Notes |
|----------|------|------------|-------|
| Id | `long` | `[DatabaseGenerated(Identity)]` | Auto-increment PK |
| AspNetUserId | `string?` | `[ForeignKey("AspNetUser")]` | FK to user |
| AspNetUser | `ApplicationUser?` | — | Navigation |
| DifficultyLevel | `int` | — | Game difficulty |
| BonesFilePath | `string?` | `[MaxLength(4096)]` | Server file path |
| OriginalFileName | `string?` | `[MaxLength(256)]` | Original file name |
| Platform | `string?` | `[MaxLength(32)]` | — |
| PlatformVersion | `string?` | `[MaxLength(32)]` | — |
| Port | `string?` | `[MaxLength(128)]` | — |
| PortVersion | `string?` | `[MaxLength(32)]` | — |
| PortBuild | `string?` | `[MaxLength(32)]` | — |
| VersionNumber | `ulong` | — | Game version |
| VersionCompatibilityNumber | `ulong` | — | Minimum compatible version |
| Created | `DateTime?` | default: `getutcdate()` | Creation timestamp |

## BonesTransaction

File: `Data/BonesTransaction.cs` — `[PrimaryKey(nameof(Id))]`

| Property | Type | Attributes | Notes |
|----------|------|------------|-------|
| Id | `long` | `[DatabaseGenerated(Identity)]` | Auto-increment PK |
| AspNetUserId | `string?` | `[ForeignKey("AspNetUser")]` | FK to user |
| AspNetUser | `ApplicationUser?` | — | Navigation |
| Date | `DateTime` | default: `getutcdate()` | Transaction timestamp |
| Type | `TransactionType` | — | Upload=0, Download=1, Deletion=2 |
| BonesId | `long` | `[ForeignKey("Bones")]` | FK to Bones table |
| Bones | `Bones?` | — | Navigation |
| DifficultyLevel | `int` | — | — |
| Platform | `string?` | `[MaxLength(32)]` | — |
| PlatformVersion | `string?` | `[MaxLength(32)]` | — |
| Port | `string?` | `[MaxLength(128)]` | — |
| PortVersion | `string?` | `[MaxLength(32)]` | — |
| PortBuild | `string?` | `[MaxLength(32)]` | — |
| VersionNumber | `ulong` | — | — |
| VersionCompatibilityNumber | `ulong` | — | — |

## SaveFileTracking

File: `Data/SaveFileTracking.cs` — `[Index(nameof(TimeStamp), nameof(AspNetUserId), IsUnique = true)]` (no explicit `[PrimaryKey]` — `Id` PK is inferred by EF Core convention)

| Property | Type | Attributes | Notes |
|----------|------|------------|-------|
| Id | `long` | `[DatabaseGenerated(Identity)]` | Auto-increment PK |
| TimeStamp | `long` | — | Part of unique composite index |
| CreatedDate | `DateTime` | — | — |
| UsedDate | `DateTime?` | — | When save was loaded |
| AspNetUserId | `string?` | `[ForeignKey("AspNetUser")]` | Part of unique composite index |
| AspNetUser | `ApplicationUser?` | — | Navigation |
| Sha256 | `string?` | `[MaxLength(64)]` | Base64-encoded SHA-256 hash |
| FileLength | `long` | — | Save file size in bytes |

## RequestInfo (Logging)

File: `Data/RequestInfo.cs` — `[PrimaryKey(nameof(Id))]`

| Property | Type | Attributes | Notes |
|----------|------|------------|-------|
| Id | `long` | `[DatabaseGenerated(Identity)]` | Auto-increment PK |
| FirstDate | `DateTime` | default: `getutcdate()` | First occurrence |
| LastDate | `DateTime` | default: `getutcdate()` | Last occurrence |
| Count | `long` | — | Identical request count |
| LastRequestId | `Guid?` | — | — |
| RequestPath | `string?` | `[MaxLength(2000)]` | — |
| Type | `LogType?` | — | See enums below |
| SubType | `RequestLogSubType?` | — | See enums below |
| Level | `LogLevel` | — | See enums below |
| Message | `string?` | — | Log message |
| RequestData | `string?` | — | Request payload data |
| RequestUserName | `string?` | `[MaxLength(256)]` | — |
| RequestAntiForgeryToken | `string?` | `[MaxLength(256)]` | — |
| ResponseCode | `int?` | — | HTTP status code |
| RequestMethod | `string?` | `[MaxLength(128)]` | — |
| UserIPAddress | `string?` | `[MaxLength(128)]` | — |
| LoginSucceeded | `bool?` | — | — |
| DecryptionSucceeded | `bool?` | — | For SFT decryption |
| AspNetUserId | `string?` | `[ForeignKey("AspNetUser")]` | — |
| AspNetUser | `ApplicationUser?` | — | — |

## Enums

```csharp
// Data/RequestInfo.cs
enum LogLevel : int { Debug = 0, Info = 1, Warning = 2, Error = 3 }
enum LogType : int { Other = 0, GameLog = 1, Bones = 2, SaveFileTracking = 3 }
enum RequestLogSubType : int {
    Default = 0, ModelStateFailed = 1, MainFunctionality = 2,
    TestConnection = 3, PartialDataError = 4, MainFunctionality2 = 5,
    CreateSaveFileTracking = 6, UseSaveFileTracking = 7
}

// Data/BonesTransaction.cs
enum TransactionType : int { Upload = 0, Download = 1, Deletion = 2 }

// Data/XLogFileLine.cs
enum OutputMode { XLog, CSV }
```

## ApplicationDbContext

File: `Data/ApplicationDbContext.cs` — inherits `IdentityDbContext`

**DbSet Properties:**
- `GameLog` → `DbSet<GameLog>`
- `Bones` → `DbSet<Bones>`
- `RequestLogs` → `DbSet<RequestInfo>`
- `BonesTransactions` → `DbSet<BonesTransaction>`
- `SaveFileTrackings` → `DbSet<SaveFileTracking>`

**OnModelCreating defaults:**
- `Bones.Created` → `getutcdate()`
- `RequestInfo.FirstDate` → `getutcdate()`
- `RequestInfo.LastDate` → `getutcdate()`
- `BonesTransaction.Date` → `getutcdate()`

**Helper: `TopScoreNumberData`** — `long DisplayIndex` (rank with ties), `long Index` (sequential).

**Method: `GetTopScoreNumberAsync(long databaseId, string? mode, string? death)`** — queries GameLog ordered by Points DESC, filtered by `Scoring == "yes"` and optional mode/death, computes ranking with tie resolution.

### Entity Relationships

```
ApplicationUser (IdentityUser)
  ├── 1:N → GameLog (AspNetUserId FK)
  ├── 1:N → Bones (AspNetUserId FK)
  ├── 1:N → BonesTransaction (AspNetUserId FK)
  ├── 1:N → RequestInfo (AspNetUserId FK)
  └── 1:N → SaveFileTracking (AspNetUserId FK)

Bones
  └── 1:N → BonesTransaction (BonesId FK)
```

## Helper Classes

### DbLogger (`Data/DbLogger.cs`)

Logs HTTP requests to the `RequestInfo` table. Implements **deduplication**: when a matching log entry exists (same UserName, Message, Type, SubType, RequestData, RequestMethod, AntiForgeryToken, RequestPath — but NOT UserIPAddress), it increments `Count` and updates `LastDate` instead of creating a new row.

**Key properties:** `LogType`, `LogSubType` (RequestLogSubType), `RequestPath`, `UserIPAddress`, `RequestMethod`, `RequestUserName`, `RequestAntiForgeryToken`, `LoginSucceeded`, `DecryptionSucceeded`, `MinLogLevel`.

**Methods:** `LogRequestAsync(string, LogLevel, int?)`, `LogRequest(string, LogLevel, int?)` (sync version).

**Constructor:** `DbLogger(ApplicationDbContext dbContext, LogLevel minLogLevel = LogLevel.Debug)`

### GnollHackHelper (`Data/GnollHackHelper.cs`, static)

- `List<string> Roles` — 13 three-letter codes: Arc, Bar, Cav, Hea, Kni, Mon, Pri, Ran, Rog, Sam, Tou, Val, Wiz
- `Dictionary<int, string> Difficulties` — { -4:"Standard", -3:"Experienced", -2:"Adept", -1:"Veteran", 0:"Expert", 1:"Master", 2:"Grand Master" }
- `Dictionary<string, string> Modes` — { "normal":"Classic", "debug":"Wizard", "explore":"Explore", "casual":"Casual", "reloadable":"Reloadable", "modern":"Modern" }
- `GetRoleText(string? roleCode, string? genderCode)` — gender-sensitive for Cav (Caveman/Cavewoman) and Pri (Priest/Priestess)
- `GetRaceText(string? raceCode)` — Hum→Human, Dwa→Dwarf, Elf→Elf, Gnl→Gnoll, Orc→Orc

### EmailSender (`Data/EmailSender.cs`)

Inherits `Azure.Communication.Email.EmailClient` and implements `IEmailSender`. Sends from `DoNotReply@gnollhack.com`.

**Static properties:** `ConnectionString`, `ConfirmAccountEmailHtml`, `ForgotPasswordEmailHtml` — all set during startup in `Program.cs`.

**Instance property:** `Azure.WaitUntil WaitUntil` — set to `Azure.WaitUntil.Started` in the parameterless constructor.

**Method:** `SendEmailAsync(string email, string subject, string htmlMessage)` — delegates to `base.SendAsync(WaitUntil, "DoNotReply@gnollhack.com", email, subject, htmlMessage)`.

### LogFileLogger (`Data/LogFileLogger.cs`)

File-based logger. Reads `LogFile` path from `IConfiguration`. Appends `{DateTime.Now sortable format}\t{message}{Environment.NewLine}` via `File.AppendAllTextAsync`. Note: uses `DateTime.Now` (local time), not UTC.

### DoubleExtensions (`Data/DoubleExtensions.cs`)

Extension method `ToPercentageString(IFormatProvider, bool nonBreakingSpace, int significantDigits)` — multiplies by 100, formats with `"G" + significantDigits`, appends `%` with optional non-breaking space.
