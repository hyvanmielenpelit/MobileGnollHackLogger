---
name: MobileGnollHackLogger External Integrations
description: Reference for all external systems that interact with MobileGnollHackLogger. Covers the NetHack Scoreboard and JunetHack xlogfile consumers, the GnollHack mobile app client API usage, and the constraints each integration imposes on the server. Use this skill when modifying API endpoints, the xlogfile format, or authentication to understand what will break.
---

# External Integrations

MobileGnollHackLogger serves three external consumers. Changes to the endpoints, formats, or authentication patterns these consumers depend on will break live integrations.

---

## NetHack Scoreboard

An external service (nethackscoreboard.org) that aggregates game results from many NetHack variants, including GnollHack.

### How It Works
- Periodically fetches `GET /xlogfile` with an HTTP `Range: bytes=N-` header
- On first fetch, reads the full response; on subsequent fetches, requests only new bytes appended since the last read
- Parses each line as tab-separated `key=value` pairs (the xlogfile format)
- Expects exactly the 47 field keys defined in `XLogFileLine._headerTexts`

### What Must Not Change
- **xlogfile output format** — `XLogFileLine.ToString(OutputMode.XLog)` field order, key names, and separator (tab)
- **HTTP Range support** — the `GET /xlogfile` endpoint's ability to serve partial content via `Range` headers
- **Byte offset stability** — the `ByteStart`/`ByteEnd`/`ByteLength` fields on `GameLog` that enable range-based seeking. Existing entries' byte positions must remain stable; new optional fields (like `seclvl`, `store`, `portseclvl`, `xplvl`) are appended only when non-null to preserve byte ranges of earlier entries.
- **Line terminator** — Unix newline (`\n`), matching Hardfought.org convention
- **Encoding** — ASCII

### Safe Changes
- Adding new `key=value` fields at the end of the xlog line (scoreboard will ignore unknown keys)
- Adding new GET endpoints that don't affect `/xlogfile`

---

## JunetHack

An annual NetHack tournament held in June (junethack.net). Players register their various NetHack variant accounts to compete.

### How It Works
- Reads from `GET /xlogfile` (same format and mechanism as NetHack Scoreboard)
- Additionally reads `GET /junethack/{username}` to map a GnollHack account name to the player's JunetHack tournament name
- Users configure their JunetHack username via the `/Identity/Account/Manage/JunetHack` page, which sets `ApplicationUser.JunetHackUserName`

### Endpoints Used
| Endpoint | Purpose | Response Format |
|----------|---------|----------------|
| `GET /xlogfile` | Game results | Tab-separated xlogfile lines |
| `GET /junethack/{username}` | Username mapping | `"junethack {JunetHackUserName}"` as `text/plain` (ASCII) |

### What Must Not Change
- Same constraints as NetHack Scoreboard for the xlogfile format
- The `GET /junethack/{username}` route and response format (`"junethack {name}"`)
- The `JunetHackUserName` field on `ApplicationUser`

---

## GnollHack Mobile App (Client)

The primary client. The GnollHack mobile game (Android, iOS, Windows, Mac) posts game data directly to this server using credentials stored in the app settings.

### Client Configuration
In the GnollHack app: **Settings → Server Posting**
- User Name and Password (the account registered on this server)
- Enable Post Top Scores (sends game logs)
- Enable Post Bones Files (participates in bones exchange)

### Endpoints Used

| Endpoint | Purpose | Auth | Data Flow |
|----------|---------|------|-----------|
| `POST /xlogfile` | Submit game result + dump logs | Yes | Client → Server |
| `POST /bones` (SendBonesFile) | Upload a bones file, receive one back | Yes | Bidirectional |
| `POST /bones` (ConfirmReceipt) | Confirm bones file was received | Yes | Client → Server |
| `POST /api/SaveFileTracking/create` | Register a new save file | Yes | Client → Server, returns encrypted ID |
| `POST /api/SaveFileTracking/use` | Mark save file as loaded | Yes | Client → Server |
| Any POST with empty payload | Test connectivity | Yes | Client → Server, expects 200 |

### Authentication Contract
- Every API call sends `UserName`, `Password`, and `AntiForgeryToken` as form data
- The `AntiForgeryToken` is a shared secret compiled into the app — changing the server's `AntiForgeryToken` config value requires a matching app update
- Authentication is per-request via `SignInManager.PasswordSignInAsync` — there are no sessions, tokens, or cookies

### What Must Not Change
- The `LoginInfoModel` form data structure (`UserName`, `Password`, `AntiForgeryToken` field names)
- All POST route paths listed above
- The `LogPostResponseInfo` JSON response from `POST /xlogfile` (JSON content served as `text/plain`)
- The bones exchange protocol: upload → receive file with `X-GH-*` headers → confirm receipt with the `X-GH-BonesFilePath` value
- The `SaveFileTracking` create/use flow and encrypted ID exchange
- The "empty payload = test connection → 200 OK" convention
- HTTP status code meanings (the app interprets specific codes: 409=duplicate, 423=banned, etc.)
