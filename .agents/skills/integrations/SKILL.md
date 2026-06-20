---
name: MobileGnollHackLogger External Integrations
description: Reference for external integrations constraints (NetHack Scoreboard, JunetHack, GnollHack mobile app). Identifies unbreakable contracts in endpoints, xlogfile formats, and auth logic.
---

# External Integrations Constraints

MobileGnollHackLogger has external consumers. Breaking these constraints breaks live services.

## 📊 NetHack Scoreboard
Aggregates game results from `GET /xlogfile`.

*   **MUST NOT CHANGE:** The xlogfile output format (`XLogFileLine.ToString(OutputMode.XLog)`). Tab-separated `key=value` format with exactly 47 standard keys.
*   **MUST NOT CHANGE:** HTTP `Range` header support on `/xlogfile`. The scoreboard relies on it to fetch only new logs.
*   **MUST NOT CHANGE:** Byte offset stability. Existing entries' byte positions must never shift.
*   **SAFE CHANGES:** Appending new `key=value` fields *at the end* of a line (the scoreboard ignores unknown keys).

## 🏆 JunetHack
Annual NetHack tournament that reads `/xlogfile` and maps player names.

*   **MUST NOT CHANGE:** Same `/xlogfile` format constraints as NetHack Scoreboard.
*   **MUST NOT CHANGE:** `GET /junethack/{username}` route. It MUST return exactly `"junethack {JunetHackUserName}"` in plain text.

## 📱 GnollHack Mobile App (Client)
The primary game client that posts data to this server.

*   **MUST NOT CHANGE:** The `LoginInfoModel` form structure (`UserName`, `Password`, `AntiForgeryToken`). The app hardcodes this authentication sequence on every POST request.
*   **MUST NOT CHANGE:** The endpoints: `POST /xlogfile`, `POST /bones`, `POST /api/SaveFileTracking/create`, `POST /api/SaveFileTracking/use`.
*   **MUST NOT CHANGE:** The Bones Exchange Protocol (`SendBonesFile` uploads and `ConfirmReceipt` deletes).
*   **MUST NOT CHANGE:** HTTP Status Code meanings. The app interprets 409 (duplicate) and 423 (banned) specifically.
*   **MUST NOT CHANGE:** "Test Connectivity" mechanism. A POST with an empty payload to these endpoints MUST return **200 OK**.
