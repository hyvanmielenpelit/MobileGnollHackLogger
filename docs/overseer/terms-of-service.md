# Terms of Service

How Overseer and the GnollHack account site serve their Terms of Service, decide which version a user
must accept, record the acceptance and enforce it. The documents themselves are wiki pages; this
describes the machinery around them. The data-protection view of the record is in
[`data-privacy-framework.md`](data-privacy-framework.md) § 8.5.

## 1. The two documents

| Key | Document | Enforced by | State |
|---|---|---|---|
| `overseer-terms` | **Gnoll Overseer Terms of Service** (privacy policy: *Gnoll Overseer Privacy Policy*) | Overseer: the chat | Live |
| `account-terms` | **GnollHack Account Terms of Service** (privacy policy: *GnollHack Account Privacy Policy*) | The account site (`MobileGnollHackLogger`): its Razor pages | Off: the page does not exist on the wiki yet |

The two are **independent**. Accepting one says nothing about the other, each has its own version, and
each host lists only the document it serves in its own `LegalDocuments:Documents` section, so Overseer
returns 404 for `account-terms` and the account site never asks about `overseer-terms`. Both write the
same table (§ 4), which is why the admin user card can show both.

The `account-terms` gate switches itself on once the page is written on the wiki and pulled into the
server's wiki clone; no configuration or code change is needed (§ 3).

Both hosts read the page from disk, from the wiki clone named by their `WikiPath` key, as
`<WikiPath>/<WikiPageName>.md`, not from the wiki retrieval index. A file is re-read when its last-write
time or length changes, so pulling the wiki takes effect on the next request without a restart. **The
account site needs `WikiPath` in its own User Secrets**, pointing at the same wiki folder Overseer
uses; it has a different `UserSecretsId` from Overseer.

### 1.1 Configuration

```json
"LegalDocuments": {
  "WikiBaseUrl": "https://wiki.gnollhack.com/",
  "VersionSource": "DocumentDate",
  "GitExecutable": "git",
  "Documents": {
    "overseer-terms": {
      "WikiPageName": "Gnoll Overseer Terms of Service",
      "PrivacyPolicyPageName": "Gnoll Overseer Privacy Policy"
    }
  }
}
```

The account site's `appsettings.json` has the same section with `account-terms` instead.

| Key | Meaning |
|---|---|
| `WikiBaseUrl` | Base of every wiki link the viewers show, and of rewritten `[[links]]` (§ 1.2) |
| `VersionSource` | `DocumentDate` (default): the page's *Last updated* line. `GitCommitDate`: the last Git commit that touched the file (§ 3) |
| `GitExecutable` | The `git` used for the Git date |
| `Documents:<key>:WikiPageName` | The page, and therefore the file name |
| `Documents:<key>:PrivacyPolicyPageName` | The privacy policy the acceptance statement links to |

### 1.2 Wiki links

The page text is served with its Gollum links rewritten to absolute Markdown links, and nothing else
changed: `[[Page]]` becomes `[Page](https://wiki.gnollhack.com/Page.md)`, and `[[label|Page]]`, **label
first, page second**, as the GnollHack wiki writes them, becomes `[label](https://wiki.gnollhack.com/Page.md)`.
The page name is URL-escaped.

## 2. Accepting

| Channel | Document | How | `Source` |
|---|---|---|---|
| GnollHack app | `overseer-terms` | The app asks `GET /api/legal/terms/overseer-terms/version`, shows the Terms itself and sends the accepted version as the `AcceptedTermsVersion` form field of `POST /api/session/create` | `GnollHackApp` |
| Web chat | `overseer-terms` | The SPA's Terms dialog in gate mode posts `POST /api/legal/terms/overseer-terms/accept` | `Web` |
| Game WebView after a handoff | `overseer-terms` | The same SPA gate, with the handoff's `sessionId`; the game's greeting waits for it (§ 6) | `Web` |
| Account site, Register | `account-terms` | A required consent checkbox, shown only while a version is required | `AccountSite` |
| Account site, Accept Terms | `account-terms` | `/Identity/Account/AcceptTerms`, where the page filter sends a signed-in user (§ 7) | `AccountSite` |

**The GnollHack app.** `AcceptedTermsVersion` is optional; app builds that predate it send nothing,
and their users accept in the chat. A version that is not the one required now records nothing. The
client version stored with the acceptance is `GHVersion` and `PortVersion` from the app's
`OverseerSettings`, joined by a space and capped at 64 characters (`SessionController.ReadClientVersion`).
**Session creation never fails because of the Terms**: `ApplyTermsAsync` catches every error, because
current app builds retry every failure except 400, 401 and 404. A user who has not accepted gets a
session whose greeting is held (§ 6).

**The web chat.** After sign-in the chat opens the gate dialog when `GET /api/auth/me` reports
`termsAccepted: false`, passing the handoff `sessionId` when there is one. The dialog cannot be
dismissed: the user checks the agreement (*I am at least 13 years old, and if I am under 18 my parent
or guardian has agreed to these Terms. I agree to the Gnoll Overseer Terms of Service, and I have read
the Gnoll Overseer Privacy Policy.*) and chooses **Accept and continue**, or **Not now**. **Not now**
disables the composer behind a banner, *To use Gnoll Overseer, accept the Terms of Service.*, with a
**Review Terms** button that reopens the gate. A `terms_not_accepted` stream event or a 403 with that
code also reopens it. A 409 `terms_version_changed` reloads the document and says *The Terms of Service
have just changed. Please review them again.*

The Terms are readable without accepting them: the public page `/terms` (no sign-in), *Terms · Privacy*
on the login page, the **Terms of Service** section of Settings (`/settings/terms`; the dialog in view
mode, or in gate mode while acceptance is pending), and the Acceptable use section of `/privacy`. The account site's footer links the Overseer
Terms always, and the account Terms while a version is required.

**The account site.** Register shows the checkbox only while `account-terms` has a required version,
and posts the version it showed; if that version changed meanwhile, the form says *The Terms of Service
were updated while you were registering. Please review them.* and unchecks the box. If recording fails
or the version changed between render and save, the account is still created and the page filter asks
the user at sign-in. Accept Terms behaves the same way, then returns to the `returnUrl`.

### 2.1 Endpoints (Overseer)

| Endpoint | Auth | Returns |
|---|---|---|
| `GET /api/legal/terms/{key}` | Anonymous | `{ key, title, version, versionLabel, markdown, contentSha256, wikiUrl, privacyPolicyUrl }`. `markdown` and `contentSha256` are null when the file cannot be read. 404 for a key this host does not serve |
| `GET /api/legal/terms/{key}/version` | Anonymous | `{ version }`, null when the gate is off. Response-cached for 300 seconds |
| `POST /api/legal/terms/{key}/accept` | Signed in; `Login` rate limit | Body `{ version, contentSha256, sessionId? }`. 200 `{ accepted: true, greetingStarted }`, also for a repeat. 409 `{ error: "terms_version_changed", requiredVersion }` when `version` is not the required one; nothing is written |
| `GET /api/legal/operator` | Anonymous | `{ statements: { <key>: { text, source, sourceUrl } } }` (§ 10) |

`GET /api/auth/me` carries `termsRequiredVersion`, `termsAccepted` (true when the gate is off),
`termsAcceptedVersion` and `termsAcceptedUtc`.

## 3. The version

**The version is the page's own *Last updated* line**, normalized to an ISO date:

| The page says | Version |
|---|---|
| `**Last updated:** 18 October 2026` | `2026-10-18` |
| `**Last updated:** 2026-10-18` | `2026-10-18` |
| `**Last updated:** 18 October 2026 (revision 2)` | `2026-10-18.2` |

- **Edit the line only for a material change.** A new version asks every user to accept again; a
  typo fix that leaves the line alone asks nobody.
- A second material change on the same day is `(revision 2)`, then `(revision 3)`. Versions compare by
  date, then revision (`LegalVersion.Compare`), so `2026-10-18.10` comes after `2026-10-18.2`; the
  strings do not sort ordinally, and SQL `MAX` is never used on them.
- Only the **first 20 lines** are searched (`LegalDocumentReader.VersionLineSearchLimit`). The bold
  markers are optional and the label is case-insensitive.
- **English month names only**, independent of the server culture. A *Last updated* line whose date is
  in neither form (`18 Oct 2026`, `October 18, 2026`) is not a version: the reader logs a warning and
  falls back to Git.

`LegalDocuments:VersionSource = "GitCommitDate"` ignores the line and makes the Git commit date the
version, so **every edit** asks again.

### 3.1 Fallbacks

| Situation | Required version | Viewer | ConfigHealth |
|---|---|---|---|
| Readable line | The line | The text | OK, with the version |
| No readable line | The date of the last commit that touched the file: `git -C <WikiPath> log -1 --format=%cs -- <file>`, 10-second timeout, `LegalDocuments:GitExecutable`. A warning is logged | The text | Warning |
| File unreadable or missing | The newest version any user has accepted, so the gate does not fall open | The wiki link only; users can still accept by version | Warning |
| Neither a file version nor any acceptance | None: **the gate is off** | The wiki link only | Warning |

The gate being off is what test hosts and fresh development machines see, and it is why `account-terms`
asks nobody today. The ConfigHealth check is one per document a host serves, id
`legal-document:<key>`, in the *Tool corpora* category, over the keys `WikiPath` and
`LegalDocuments:Documents:<key>:WikiPageName`.

## 4. The record

Table `UserTermsAcceptances` (migration `AddUserTermsAcceptance`, entity `UserTermsAcceptance`):

| Column | Meaning |
|---|---|
| `Id` | Key |
| `AspNetUserId` | The user; foreign key with **cascade delete** |
| `DocumentKey` | `overseer-terms` or `account-terms` |
| `Version` | `2026-10-18`, or `2026-10-18.2` |
| `ContentSha256` | Lower-case hex SHA-256 of the file bytes served. The web gate echoes the `contentSha256` it received with the document; the account site computes it at acceptance. **Null for GnollHack app acceptances** |
| `AcceptedUtc` | When |
| `Source` | `Web`, `GnollHackApp` or `AccountSite` |
| `ClientVersion` | The app version, for `GnollHackApp` acceptances |

- **Append-only.** A row is never updated. A unique index on (`AspNetUserId`, `DocumentKey`, `Version`)
  makes a repeat, or a double submit, write nothing.
- **No IP address and no user agent** are recorded.
- **Deleted with the account**, by cascade (`AccountErasure`).
- **Included in both personal-data exports** as `termsAcceptances` (document, version, time, source;
  oldest first; `ChatDataExport.BuildTermsAcceptancesAsync`): the account site's *Download personal
  data* and Overseer's `GET /api/privacy/export`.

### 4.1 Looking up a user

- **Admin → Users**: the user card's *Overseer Terms* and *Account Terms* lines
  ([`admin-user-management.md`](admin-user-management.md) § 1).
- **SQL**:

  ```sql
  SELECT DocumentKey, Version, AcceptedUtc, Source
  FROM UserTermsAcceptances
  WHERE AspNetUserId = '<id>'
  ORDER BY AcceptedUtc
  ```

## 5. Enforcement in Overseer

`TermsAcceptanceStore.GetStatusAsync` caches each user's status **for 10 minutes per user and document**,
keyed together with the required version, so a new version applies on the user's next request. An
acceptance evicts the entry.

| Where | What happens |
|---|---|
| `ChatController.Send` | `[RequireTermsAccepted]`, ordered after `[RequireOverseerAccess]` so a blocked user sees the block: 403 `{ error: "terms_not_accepted", requiredVersion }` |
| `ChatService.GenerateAndBroadcastMessageAsync` | The entry point of both the chat's send and the handoff's background greeting. A user who has not accepted gets an `error` event with code `terms_not_accepted` and the message *To use Gnoll Overseer, accept the Terms of Service.* A failed status lookup lets the turn run |
| `POST /api/session/create` | Never refused; the greeting is held instead (§ 6) |

GnollBench runs do not use `GenerateAndBroadcastMessageAsync` and are not gated. Reading, exporting and
deleting chats, Settings, and every other endpoint stay available to a user who has not accepted.

The **game upload APIs are not gated**: they are controllers, and the account site's filter (§ 7) is a
Razor page filter.

## 6. The held greeting

When `/api/session/create` carries an initial prompt for a user who has not accepted the Overseer
Terms, `PendingGreetingStore` holds the prompt in memory for **30 minutes**, keyed by session and user,
so one user cannot start another's greeting. It is **lost on restart**.

When the user accepts in the game's WebView, the SPA sends the handoff `sessionId` with the accept
request. `LegalController` takes the held prompt, checks that the user owns the session, and starts the
greeting turn through `HandoffGreetingStarter`, under the same rate limit, concurrency gate and timeout
as any other turn. The response's `greetingStarted` says whether it did; it is false when nothing was
held, the hold expired or the server restarted.

## 7. Enforcement on the account site

`RequireAccountTermsPageFilter` runs on every Razor page. A signed-in user who has not accepted the
required `account-terms` version is redirected to `/Identity/Account/AcceptTerms?returnUrl=<requested
URL>`. Anonymous requests pass through, and controllers are unaffected. A page model carrying
`[AllowWithoutAccountTerms]` is exempt:

| Page | Why |
|---|---|
| `Account/AcceptTerms` | The acceptance page itself |
| `Account/Logout` | Leaving is always possible |
| `Account/Manage/PersonalData` | The personal data page |
| `Account/Manage/DeletePersonalData` | Deleting the account never needs consent |
| `Account/Manage/DownloadPersonalData` | Nor does a copy of one's data |
| `Error` | The error page |
| `Privacy` | The privacy page |

While no `account-terms` version is required, the filter lets everything through.

## 8. Changing the Terms

1. Edit the page on the wiki. For a **material** change, set **Last updated** to the day's date
   (`(revision N)` for a second one that day); otherwise leave the line alone.
2. Pull the wiki clone on the server. The new version applies on the next request; no restart.
3. Check Admin → Services → Configuration Health (`/admin/services/config-health`): the
   `legal-document:<key>` check reports the version, and is a warning if the line could not be read.

Every user is then asked once for the new version, through whichever channel they use next.

## 9. Not covered

- The record proves that an account accepted a version, not who was at the keyboard.
- The web hash is the one the browser was served and sent back; the server stores it as received.
- A user who never opens the chat again is never asked, and keeps the last version they accepted.

## 10. Operator statements

The privacy notice's § 7 *Operator statements* lists statements only the operator of a deployment can
make. They live in `PrivacyNotice:OperatorStatements` in Overseer's `appsettings.json`, one entry per
key, each `{ "Text": …, "Source": …, "SourceUrl": … }`:

| Key | Statement |
|---|---|
| `legalEntity` | The legal entity responsible for the deployment |
| `contact` | A contact address for data-subject requests |
| `dataProcessingAgreement` | Whether a data processing agreement is offered |
| `breachNotification` | How a personal-data breach is assessed and notified |
| `dataResidency` | Where data is stored and inference runs |
| `telemetryRetention` | How long error telemetry is kept |
| `acceptableUse` | The operator's own acceptable-use terms |

`GET /api/legal/operator` serves them; an entry without `Text` is left out, and the notice renders that
item as **Not supplied by the operator** with its static description. `Source` names where a statement
comes from and `SourceUrl`, when set, links it. **Changing a statement needs only a restart of
Overseer, no client build.** All seven are configured on this deployment.
