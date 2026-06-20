---
name: MobileGnollHackLogger API Endpoints
description: Reference for Web API controllers (LogController, BonesController, DumpLogController, JunetHackController, SaveFileTrackingController). Covers routes, auth logic, and response formats.
---

# API Endpoints Reference (`Areas/API/`)

## 🚨 Authentication Contract (Applies to all POST endpoints)
*   **NO TOKENS/COOKIES.** Auth is per-request. Credentials (`UserName`, `Password`, `AntiForgeryToken`) are sent in form data with every API call.
*   **Step 1:** Validate `AntiForgeryToken` against configuration → **401** if invalid.
*   **Step 2:** `SignInManager.PasswordSignInAsync` → **403** if failed, **412** if 2FA required, **423** if locked out.
*   **Step 3:** Check user bans (`IsBanned`, `IsGameLogBanned`, `IsBonesBanned`) → **423**.
*   **Step 4:** Execute business logic.
*   **Step 5:** Log all requests to DB via `DbLogger`.

## 📝 LogController (`/xlogfile`)
*   **GET `/xlogfile`**: Returns all game logs. Supports HTTP `Range` headers using `GameLog.ByteStart`/`ByteEnd`. Critical for external integrations.
*   **POST `/xlogfile`**:
    *   **Input:** `PlainTextDumpLog`, `HtmlDumpLog`, `XLogEntry`.
    *   **Logic:** Replaces `XLogFileLine.Name` with authenticated `UserName` to prevent spoofing. Saves dump logs to disk. Saves `GameLog` to DB.
    *   **Response:** Plain text JSON (`LogPostResponseInfo`).
    *   **Test Connection:** Sending empty fields returns **200 OK**.

## 📄 DumpLogController (`/dumplog`)
*   **GET `/dumplog/{id:int}`** or `/dumplog/byname/{name}/{starttime}`
*   **Logic:** Reads dump logs from `{DumpLogPath}/{Name}/gnollhack.{Name}.{StartTimeUTC}.{ext}`. Returns 404 or 410 if missing.

## ☠️ BonesController (`/bones`)
*   **POST `/bones` (Command="SendBonesFile")**: Uploads a bones file. Checks limits. Selects a compatible bones file to return (matching difficulty, compatible version, respecting whitelist/blacklist). Returns `application/octet-stream` with `X-GH-*` headers.
*   **POST `/bones` (Command="ConfirmReceipt")**: Deletes the bones file the server just sent to the client (path provided in `Data`).
*   **Test Connection:** Empty `Data` and `BonesFile` returns **200 OK**.

## 🏆 JunetHackController (`/junethack/{username}`)
*   **GET:** Returns `"junethack {JunetHackUserName}"` as plain text. Used by external JunetHack scoreboard.

## 💾 SaveFileTrackingController (`/api/SaveFileTracking`)
*   **POST `/api/SaveFileTracking/create`**: Registers a new save file. Checks for duplicate `TimeStamp + AspNetUserId`. Returns an AES-256-CBC encrypted DB ID.
*   **POST `/api/SaveFileTracking/use`**: Decrypts ID, validates `TimeStamp`, `Sha256`, `FileLength`, `UserName`. Marks file as used. Prevents save scumming.

## 🎥 ReplayController (`/api/replay`)
*   **POST:** Authenticated stub endpoint for future replay uploads.
