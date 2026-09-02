# Chat Message and Data Retention Specification

This document details the architectural specification, data model lifecycle, business rules, automated background maintenance, and telemetry monitoring for chat sessions, messages, and disk attachments in Overseer.

---

## 1. Overview & Operational Context

Overseer shares an operational Microsoft SQL Server instance with GnollHack game logs and telemetry. SQL Server Express imposes a physical per-database limit on data files, and that limit **depends on the product version**.

The limit is **not hard-coded**. `DatabaseStorageMetricsService` detects the instance's edition and version at runtime via `SERVERPROPERTY`, and `SqlServerCapacity` (`Overseer/Models/SqlServerCapacity.cs`) maps the result onto a ceiling:

| Instance | Data-file limit |
|---|---|
| SQL Server 2025 Express and later (major version ≥ 17) | **50 GB (51,200 MB)** |
| SQL Server 2008 R2 – 2022 Express (major version 10.50 – 16) | **10 GB (10,240 MB)** |
| SQL Server 2008 Express and earlier | 4 GB (4,096 MB) |
| Standard, Developer, Enterprise, Azure SQL | No engine-imposed per-database limit |
| Detection failed, or version unavailable | 10 GB assumed, and reported as `Fallback` |

The instance in production at the time of writing is **SQL Server 2022 Express**, so the effective limit is 10 GB. Detection is cached for one hour (five minutes after a failure).

The limit applies to the **sum** of the database's data files (`sys.database_files WHERE type = 0`); the transaction log, FILESTREAM containers, and full-text files are not counted against it.

To prevent runaway database and disk growth while maintaining an optimal user experience, Overseer implements a comprehensive, multi-tiered retention and automated lifecycle management system covering:
- **Relational Data**: `ChatSession`, `ChatMessage`, `ChatMessageToolCall`, `ChatMessageAttachment`.
- **Disk Attachments**: Uploaded images, audio, and documents stored in `<ConversationsDataLocation>/<SessionId>/`.

---

## 2. Session Lifecycle & State Machine

Every chat session traverses a deterministic state machine:

| Lifecycle State | Description | Transition Triggers | Data Retention |
|---|---|---|---|
| **Active (Unpinned)** | Standard active chat visible in user sidebar. | Session created; or unpinned by user. | Retained until user deletes, active quota is exceeded (>50), or inactive for >90 days. |
| **Active (Pinned)** | Pinned chat positioned at top of sidebar. | User pins active chat (max 5 pinned). | **Immune** to automated inactivity pruning and quota eviction. |
| **Soft-Deleted (Trash)** | Chat hidden from sidebar, accessible in Trash modal. | User deletes chat; quota eviction (`"Quota"`); or inactivity TTL (`"Inactivity"`). | Retained for 30 days (`SoftDeleteGracePeriodDays`). Can be restored if quota permits. |
| **Permanently Purged** | Hard-deleted from SQL Server database and file system. | 30-day grace period expires; user empties trash; or admin triggers purge. | Irreversible removal of all relational rows and disk directory. |

---

## 3. Configuration & Business Rules

All retention policies are configured under `"ChatRetentionSettings"` in `appsettings.json` and bound to the `ChatRetentionSettings` model class:

```json
"ChatRetentionSettings": {
  "MaxActiveSessionsPerUser": 50,
  "MaxPinnedSessionsPerUser": 5,
  "InactivityTtlDays": 90,
  "SoftDeleteGracePeriodDays": 30,
  "PruneToolCallResultsDays": 30,
  "MaintenanceRunHourUtc": 3,
  "DatabaseMaxSizeMbOverride": 0,
  "DatabaseWarningThresholdPercent": 75,
  "DatabaseCriticalThresholdPercent": 85,
  "EnableStorageWarningEmails": true
}
```

### Policy Reference Table

| Rule | Configuration Key | Default | Enforcement Details |
|---|---|---|---|
| **Active Session Cap** | `MaxActiveSessionsPerUser` | `50` | When a user creates a new chat or sends a message, if `ActiveCount > 50`, the oldest unpinned session (`OrderBy(LastMessageUtc)`) is moved to Trash with `DeletionReason = "Quota"`. |
| **Pinned Session Cap** | `MaxPinnedSessionsPerUser` | `5` | Users may pin up to 5 chats. Pin attempts beyond 5 are rejected with an error. Pinned chats cannot be evicted by quota or inactivity. |
| **Inactivity Soft-Delete** | `InactivityTtlDays` | `90` | Active unpinned sessions with no message activity for > 90 days are soft-deleted with `DeletionReason = "Inactivity"`. |
| **Trash Grace Period** | `SoftDeleteGracePeriodDays` | `30` | Soft-deleted sessions remain recoverable in Trash for 30 days. After 30 days, they are permanently purged during daily maintenance. |
| **Tool Payload Pruning** | `PruneToolCallResultsDays` | `30` | Large JSON payloads in `ChatMessageToolCall.Result` and `ArgsText` older than 30 days are set to `NULL` to reclaim table space. Message history and transcripts remain intact. |
| **Daily Schedule** | `MaintenanceRunHourUtc` | `3` (03:00 UTC) | Automated maintenance pass runs daily at 03:00 UTC (and 60 seconds after server startup). |
| **Size Limit Override** | `DatabaseMaxSizeMbOverride` | `0` (auto-detect) | Forces the capacity ceiling in MB, overriding detection. Use it to pre-configure a limit before an upgrade, or to budget Overseer to less than the engine allows on a shared instance. The panel labels an overridden ceiling as a *Budget* rather than a *Limit*. |
| **Warning Threshold** | `DatabaseWarningThresholdPercent` | `75` (%) | Percentage of the resolved limit that triggers a Warning email report to `ReportEmailAddress` (throttled to 1 email/24h). |
| **Critical Threshold** | `DatabaseCriticalThresholdPercent` | `85` (%) | Percentage of the resolved limit that triggers a Critical email report (throttled to 1 email/24h). |
| **Absolute Warning Trip** | `DatabaseWarningThresholdMb` | `0` (disabled) | Optional absolute ceiling in MB, evaluated *in addition* to the percentage. The more severe of the two classifications wins. |
| **Absolute Critical Trip** | `DatabaseCriticalThresholdMb` | `0` (disabled) | As above, for the Critical level. |

> [!NOTE]
> The thresholds are percentages so that they stay meaningful when the limit changes. On the
> current 10 GB instance, 75 % and 85 % are exactly 7,680 MB and 8,704 MB — the absolute values
> used before this change — so behaviour is unchanged to the megabyte. Out-of-range or inverted
> percentages fall back to 75/85 and are logged as a warning at startup.

### Upgrading the SQL Server Instance

Upgrading the engine — for example from SQL Server 2022 Express to 2025 Express — requires **no code or configuration change**. Restart Overseer (or wait up to one hour for the detection cache to expire) and confirm the Admin page's **Database** tab now reads *SQL Server 2025 Express Capacity (50 GB Limit)* with the provenance line `limit auto-detected`.

If the panel still reports the old limit, the provenance line names the reason:
- `limit set by configuration override` — `DatabaseMaxSizeMbOverride` is still set; clear it to `0`.
- `limit not detected — assuming 10 GB` — the `SERVERPROPERTY` query failed; check the application log for the detection error.

---

## 4. Maintenance Execution Logic

### 1. Daily Background Service (`DatabaseMaintenanceBackgroundService`)
- Implemented as an ASP.NET Core `BackgroundService`.
- **Initial Warm-up**: Runs an initial maintenance pass 60 seconds after application startup to clear any accumulated backlog.
- **Daily Loop**: Schedules next execution for 03:00 UTC daily.
- **Error Handling**: Wrapped in resilient try-catch logic with automatic 1-hour retry on unexpected database failures.

### 2. Full Maintenance Pass (`RunFullMaintenanceAsync`)
When executed (either automatically by the background service or manually from the Admin dashboard), the maintenance engine carries out 5 distinct phases in sequence:

1. **Soft-Delete Inactive Chats**:
   - Queries `ChatSession` where `!IsDeleted && !IsPinned && LastMessageUtc < (Now - InactivityTtlDays)`.
   - Updates `IsDeleted = true`, `DeletedUtc = Now`, `DeletionReason = "Inactivity"`.
2. **Identify Expired Trash**:
   - Queries `ChatSession` where `IsDeleted && DeletedUtc < (Now - SoftDeleteGracePeriodDays)`.
3. **Purge Expired Trash & Attachments**:
   - For each expired session ID, deletes the folder `<ConversationsDataLocation>/<SessionId>` from physical disk.
   - Executes bulk delete in relational dependency order:
     1. `ChatMessageAttachment`
     2. `ChatMessageToolCall`
     3. `ChatMessage`
     4. `ChatSession`
4. **Prune Aged Tool Results**:
   - Updates `ChatMessageToolCall` where message timestamp is older than `PruneToolCallResultsDays`, setting `Result = NULL` and `ArgsText = NULL`.
5. **Sweep Orphaned Disk Folders**:
   - Iterates through all subfolders in `ConversationsDataLocation`. If the numeric folder ID has no corresponding entry in `ChatSession`, the folder is deleted.

---

## 5. Storage Metrics & Telemetry

The `DatabaseStorageMetricsService` queries live database DMVs and physical file allocations:

### Dynamic SQL Queries
- **Database Allocation**: Queried from `sys.database_files` (`size * 8 / 1024.0` for allocated MB, `FILEPROPERTY(name, 'SpaceUsed')` for used MB).
- **Table Allocations**: Queried from `sys.tables`, `sys.indexes`, `sys.partitions`, and `sys.allocation_units` for top tables (`ChatSession`, `ChatMessage`, `ChatMessageToolCall`, `ChatMessageAttachment`, `GameLog`, `RequestInfo`, `SystemAiUsageLog`, `Bones`).
- **Disk Attachment Scanning**: Aggregates directory counts, file counts, and total byte size within `ConversationsDataLocation`.

### Admin Dashboard UI
The Admin **Database** tab exposes:
- Real-time gauge and status badge (`Normal`, `Warning`, `Critical`).
- Active, Pinned, Inactive, and Trash session counters.
- Estimated reclaimable disk and database space.
- Granular manual action buttons with interactive confirmation dialogs, centered loading modals, and toast notifications.

---

## 6. Business Logic Validation & Test Suite

Unit tests in `Overseer.Tests/UnitTests/ChatRetentionServiceTests.cs` validate all core retention behaviors:
- Quota eviction ordering (oldest unpinned session evicted first).
- Pinned session immunity against quota eviction and inactivity TTL sweeps.
- Pinned session quota limits (maximum 5).
- Restoring sessions under quota constraints.
- Cascade deletion order and disk folder cleanup.
- Tool call result pruning without altering chat transcripts.
- Dry-run verification (no data modified when `DryRun = true`).
