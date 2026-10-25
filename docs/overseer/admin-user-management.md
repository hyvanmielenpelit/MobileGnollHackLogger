# Admin User Management

How an Overseer administrator finds, inspects and changes GnollHack accounts: the Users tab's status
badges and filters, the per-user actions and their confirmations, the Bulk Actions wizard, the durable
admin log, and the sign-in rules those actions rely on, including why a GnollHack client mistyping a
password never locks an account. Security events, the AI-chat block, deletion holds and the
self-service deletion review are in [`security-monitoring.md`](security-monitoring.md).

An **administrator** is an account whose user name is in the comma-separated `Admins` configuration
value; there are no Identity roles. Every route below is `[Authorize(Policy = "AdminOnly")]` and
answers 401 to an anonymous caller and 403 to a signed-in non-administrator.

## 1. The Users tab

Admin → **Users** has four sub-tabs, *Users · Groups · Deletion requests (N pending) · Admin log*
(`/admin/users`, `/admin/users/groups`, `/admin/users/deletions`, `/admin/users/log`; the scheme is in
[`admin-page-urls.md`](admin-page-urls.md)), and a gold **Bulk Actions** button that opens the wizard
of § 5 seeded with the list's current filters. **Groups** lists the user groups with **Create**; each
group's gear button opens *AI configuration access for group {group}*.

The **Users** sub-tab is a card list fetched from the server in batches of 20 (**Show 20 more**). Its
filter bar is shared with step 1 of the wizard:

- **Search** by user name **or** email (contains, case-insensitive).
- **Status** (any of the statuses in § 2), **Email** (*Confirmed* / *Not confirmed*), **Registered**
  (*In the last 7 days*, *7–30 days ago*, *More than 30 days ago*, *More than a year ago*), **Last
  sign-in** (*In the last 30 days*, *In the last 90 days*, *More than 90 days ago*, *More than a year
  ago*, *Never recorded*) and **Data** (*Has / No game data*, *Has / No Overseer data*). The counts
  follow the search only, not the other facets.
- **Sort by**: *Newest first* (default), *Oldest first*, *User name (A–Z)*, *Email (A–Z)*, *Signed in
  recently*, *Longest without sign-in*; an unknown date sorts last. The choice is kept per browser
  (`overseer.admin.users.view`).

Each card shows the user name and email; *Registered 2026-03-02* (or *Registered on or before …* for a
backfilled date, § 2.1) and the last sign-in (*not recorded* while unknown); the status badges, each a
glyph and a word with its facts in a tooltip; the failed sign-in count, two-factor state, groups, game
logs and bones, chats, and the Junethack user name (*None* when unset); the **Overseer Terms** and **Account Terms** lines (below); icon buttons for groups (*Manage Groups for {user}*), AI configurations
(*AI configuration access for {user}*: one card per system configuration with its switch, badges and
per-assignment state, and the Rate limits and Overrides dialogs) and the user's security events;
and **Actions**, a popover of the actions in § 3 grouped under *Access*, *Account* and *Deletion*. An
action the server would refuse is shown unavailable with its reason. After an action the card is
replaced by the server's copy and carries a *before → after* note; after a delete it becomes a
tombstone until the next reload.

The two Terms lines show the newest version of each Terms of Service document the user has accepted,
with its date and source (`Web`, `GnollHackApp` or `AccountSite`), or that it is not accepted: **Overseer
Terms** for the Gnoll Overseer Terms of Service (`overseer-terms`) and **Account Terms** for the
GnollHack Account Terms of Service (`account-terms`). The server sends them as
`AdminUserDto.TermsAcceptances`, one `AdminTermsAcceptanceDto { document, version, acceptedUtc, source }`
per document accepted. A line shows the newest version accepted, which is not necessarily the version
required now. There is no list column, filter or sort for the Terms; the record and how versions work
are in [`terms-of-service.md`](terms-of-service.md).

## 2. Statuses

`AdminUserStatus.Evaluate` decides the statuses of one account in memory and `AdminUserStatus.Apply`
filters a list in SQL; `AdminUserStatusTests` pins that the two agree on every combination. They are
listed in this order:

| Status | Label | True when |
|---|---|---|
| `GoodStanding` | In good standing | the email is confirmed and none of the next seven holds |
| `LockedOut` | Locked out | `LockoutEnd > now` (admin-set or after failed sign-ins) |
| `Banned` | Banned | `IsBanned` |
| `GameLogBanned` | Game log uploads banned | `IsGameLogBanned` |
| `BonesBanned` | Bones banned | `IsBonesBanned` |
| `AiChatBlocked` | Blocked from AI chat | `OverseerBlockedUtc != null` |
| `DeletionHold` | Deletion hold | an active `AccountHold` |
| `DeletionPending` | Deletion pending | a `Pending` `AccountDeletionRequest` |
| `EmailUnconfirmed` | Email not confirmed | `!EmailConfirmed` |
| `Administrator` | Administrator | the user name is in `Admins` |

*Administrator* is not a problem and does not affect *In good standing*. A lockout is **admin-set**
while `LockoutEnd == AdminLockoutEndUtc` and it is in the future; any other lockout is an automatic one
after failed sign-ins, so an admin's old reason never shows on a later automatic lockout.

**Has game data** is any `GameLog`, `Bones` or `SaveFileTracking` row. **Has Overseer data** is any
chat, `SystemAiUsageLog` or `UserKeyAiUsageLog` row, or a benchmark row the account started, created or
computed.

### 2.1 Account columns

`ApplicationUser` carries these nullable columns (migration `AdminUserManagement`):

| Column | Meaning |
|---|---|
| `CreatedUtc` | When the account was registered; set by the website's *Register* and *External login* pages |
| `CreatedUtcIsEstimate` | True when `CreatedUtc` was backfilled |
| `LastSignInUtc` | The last successful sign-in on any surface |
| `BannedUtc`, `BanReason`, `BannedByUserId` | Set with `IsBanned` by an admin; null on a ban set before this column existed |
| `AdminLockoutEndUtc`, `AdminLockoutReason`, `AdminLockoutByUserId` | The lockout an admin set |

**`CreatedUtc` of an account that existed before the migration is an upper bound.** The migration set
it to the earliest of the account's first game log, its first chat and the migration time, with
`CreatedUtcIsEstimate = 1`, and the card reads *on or before*. An age criterion therefore errs toward
protecting: *registered more than 30 days ago* never becomes true earlier than the real date would make
it, and an account without a date never satisfies it.

**`LastSignInUtc` is new, so every account reads *Never recorded* until its next sign-in.** It is
written by `AccountActivity.TouchSignInAsync`, at most once an hour per account, after every completed
cookie sign-in on the website and in Overseer (password, two-factor, recovery code and external, through
`GnollHackSignInManager`), every successful game API sign-in and every successful
`/api/session/create`. Until accounts have signed in after deployment:

- the *Last sign-in* criteria and sorts are not meaningful — *Never recorded* also matches accounts in
  daily use;
- the bulk delete's *signed in within 90 days* protection protects nothing yet.

The other bulk delete protections hold from the start: an account with any game or Overseer data is
always refused, and an account with a confirmed email is only ever deleted behind *Advanced* (§ 5.2).

## 3. Per-user actions

Every action loads the account, refuses a protected target, changes state, writes one
`AdminUserActions` row (§ 6) and, where the table says so, a security event, and returns
`AdminUserActionResultDto { user, before, after, summary, deleted }`: the refreshed card, the statuses
before and after with their text, and a one-line summary. The guard refuses the administrator's own
account with 409 `self` and another administrator with 409 `administrator`, except where the table
says otherwise. An action with nothing to change answers 409 `already-done`.

| Action | Route | Body | Effect | Guard | Event |
|---|---|---|---|---|---|
| Lock out | `POST {id}/lockout` | `{ until, reason }` | `LockoutEnd = until`, lockout enabled, the `AdminLockout*` columns, a new security stamp | self, admins | `AdminLockedOut` (45) |
| Unlock | `DELETE {id}/lockout` | — | Clears `LockoutEnd`, `AccessFailedCount` and the `AdminLockout*` columns; ends any lockout, automatic ones included | self, admins | `AdminUnlocked` (46) |
| Ban | `POST {id}/ban` | `{ reason }` | `IsBanned`, `BannedUtc`, `BanReason`, `BannedByUserId`, a new security stamp | self, admins | `AdminBanned` (47) |
| Unban | `DELETE {id}/ban` | — | Clears the four | self, admins | `AdminUnbanned` (48) |
| Game upload restrictions | `PUT {id}/game-restrictions` | `{ gameLogBanned, bonesBanned, reason? }` | `IsGameLogBanned`, `IsBonesBanned` | none: self and admins allowed | — |
| Sign out everywhere | `POST {id}/sign-out-everywhere` | — | A new security stamp | other admins only; self allowed | `AdminSignedOut` (49) |
| Mark email confirmed | `POST {id}/confirm-email` | — | Confirms the email through a generated token | self, admins | `AdminEmailConfirmed` (50) |
| Reset two-factor | `POST {id}/reset-two-factor` | `{ reason? }` or empty | Two-factor off, a new authenticator key (which renews the security stamp) | self, admins | `AdminTwoFactorReset` (51) |
| Change email | `POST {id}/change-email` | `{ email, markConfirmed, reason }` | `SetEmailAsync` (the address unconfirmed, a new security stamp), then confirmed through a generated token when `markConfirmed`; the user name is unchanged | self, admins | `AdminEmailChanged` (52) |
| Block from AI chat | `POST {id}/overseer-block` | `{ reason }` | `OverseerAccessService.BlockAsync` | self, admins | `AdminBlocked` |
| Unblock from AI chat | `DELETE {id}/overseer-block` | — | `OverseerAccessService.UnblockAsync` | none | `AdminUnblocked` |
| Place deletion hold | `POST {id}/account-hold` | `{ reason }` | `AccountDeletionReviewService.PlaceHoldAsync` | none | `AccountHoldPlaced` |
| Release deletion hold | `DELETE {id}/account-hold` | — | `AccountDeletionReviewService.ReleaseHoldAsync` | none | `AccountHoldReleased` |
| Delete account | `GET {id}/deletion-impact`, `POST {id}/delete` | § 4 | § 4 | self, admins, § 4 | — |

**Reasons.** Lock out, ban, change email and delete need a reason of 1–512 characters (400 `reason`);
game restrictions and two-factor reset take an optional one of at most 512; the AI-chat block takes 1–256
characters, which the user is shown; a deletion hold takes 1–512, which the user is never shown. No
other reason reaches the user.

**Two-factor.** The website does not offer two-factor setup: the game API refuses a two-factor account
(§ 7.2, 412), so an account that turned it on could no longer play online. Its *Enable authenticator* and
*Reset authenticator* pages exist only to shadow the Identity UI pages at those paths, and redirect to
*Two-factor authentication*. A user who already has two-factor can still disable it and regenerate
recovery codes on the website. *Reset two-factor* is the administrator's recovery path; afterwards the
account signs in with the password alone.

**Change email.** The address must be a valid email address of at most 256 characters (400 `email`),
and another account must not already use it (409 `email-taken`). Unless `markConfirmed`, Overseer
emails the **new address only** instructions to confirm it from the account website's *Resend email
confirmation* page, `AdminUserManagement:AccountSiteBaseUrl` + `/Identity/Account/ResendEmailConfirmation`,
and why that step is needed. The email carries no confirmation link: Overseer and the website protect
tokens with separate Data Protection key rings, so a token made in Overseer would not validate on the
website. No address goes into the admin log, the security event or a log line. A failed send, or no
email sender configured (`ConnectionStrings:EmailConnection`), leaves the change in place, and the
summary tells the administrator to pass the instructions on.

**Lockout end.** `until` must be in the future and at most `AdminUserManagement:LockoutMaxDays` (365)
ahead, else 400 `until`; a longer exclusion is a ban.

**`already-done`.** Unlock on an account that is not locked out, has no failed sign-ins and no admin
lockout columns; ban on a banned account; unban on one that is not; mark email confirmed on a confirmed
one; game restrictions that change nothing; placing a deletion hold on an account that already has one;
changing the email to the current address, compared normalized.
Releasing a hold the account does not have answers 404.

The block and hold routes and bodies are unchanged from when they lived in `AdminController`; they now
return the result DTO instead of an empty 200, and their services write the audit row.

### 3.1 What each action does where

| Action | Website | Overseer | GnollHack (game APIs and the Overseer page) |
|---|---|---|---|
| Lock out | Sign-in shows the Lockout page | Sign-in refused, *temporarily locked* | 403 *"This account is temporarily locked. Try again in {n} minutes."*, or *"This account is locked until {yyyy-MM-dd HH:mm} UTC."* beyond 24 hours; session-create 401 `locked_out` |
| Ban | No sign-in on any path; *"This account has been banned."* after the correct password | The same, 401 | 403 *"This account has been banned."*; session-create 401 `banned` |
| Game upload restrictions | Sign-in unaffected | Unaffected | `/xlogfile` refuses a game-log-banned account with 423; `/bones` refuses a bones-banned one with 423, and its bones are never sent to other players |
| Sign out everywhere, reset two-factor | Signed-in sessions end within 5 minutes | The same | GnollHack sends the password with every request, so it keeps working |
| Mark email confirmed | Sign-in allowed | Sign-in allowed | Allowed |
| Change email | Sign-in refused as unconfirmed until the user confirms the new address, unless marked confirmed; signed-in sessions end within 5 minutes | The same | Refused as unconfirmed (403) until confirmed, unless marked confirmed |

Locking out, banning, signing out everywhere, resetting two-factor and changing the email renew the security stamp; signed-in
website and Overseer sessions notice it within `IdentityLockout:SecurityStampValidationMinutes` (5) and
end, an open Overseer chat connection no later than its first hub call after that interval. A ban and a
lockout also refuse every new sign-in at once.

### 3.2 Confirmation

Friction follows the damage an action can do:

| Level | Actions | Dialog |
|---|---|---|
| Safe | Unlock, Unban, Unblock from AI chat, Release deletion hold, Sign out everywhere, Mark email confirmed | The shared confirm dialog: one sentence on what happens, a button named for the verb |
| Caution | Block from AI chat, Place deletion hold, Reset two-factor (do it only after verifying the person's identity) | The shared reason dialog |
| Caution | Lock out | Duration (*1 hour*, *24 hours*, *7 days*, *30 days*, *Until a date…*), a required reason, the effect line and *"I have checked this is the right account ({email})."* |
| Caution | Ban | A required reason, *"Cannot sign in anywhere until unbanned. The reason is not shown to the user."* and the same checkbox |
| Caution | Change email | The current address, *New email address*, *Mark the new address as confirmed* (off by default), the effect line, a required reason and the same checkbox |
| Caution | Game upload restrictions | *Ban game log uploads*, *Ban bones uploads and downloads*, and a reason the dialog requires whenever anything is turned on |
| Danger | Delete account | § 4 |

The Unlock confirmation says that a game sign-in block after repeated wrong passwords can last up to 15 more minutes, because Unlock does not clear the GnollHack sign-in throttle (§ 7.3).

## 4. Deleting one account

`GET {id}/deletion-impact` returns what a deletion would erase and why it would be refused:

- `refusals[] { code, message }`: `self`, `administrator`, `hold` (*Release the deletion hold first.*),
  `pending-request` (decide it in *Deletion requests*), `erasure-paths` (Overseer's `DumpLogPath` or
  `ConversationsDataLocation` is missing). With any refusal the dialog shows only the reasons and
  **Close**; no delete control is rendered.
- `categories[] { key, label, count, important, note }`:

| Key | Label | Important | What happens |
|---|---|---|---|
| `game-logs` | Game logs | yes | Scrubbed and detached; their top-score entries disappear |
| `bones` | Bones | yes | Rows and files deleted |
| `save-files` | Save-file tracking | yes | Deleted |
| `chats` | Overseer chats | yes | Deleted with their messages and attachments |
| `ai-usage` | AI usage records | no | Deleted |
| `security-events` | Security events | no | Deleted; the admin log keeps its record |
| `groups` | Group memberships | no | Removed |
| `configurations` | Configuration assignments | no | Removed |
| `benchmark-records` | Benchmark records | no | Kept; the attribution is removed |

`POST {id}/delete` takes `{ confirmUserName, reason, acknowledgedCategories[] }`. The server recomputes
the impact and refuses:

- any refusal, as 409 with its code;
- a typed name that is not ordinal-equal to the trimmed user name: 400 `confirm-name`;
- `acknowledgedCategories` that is not exactly the important keys with a count above zero: 400
  `acknowledgments`, so a dialog opened before the account gained data never deletes more than it
  showed;
- a missing reason: 400 `reason`.

It then writes the audit row **before** erasing, with the user name and every count in its detail
(*"Deleted bob. Game logs: 12, Bones: 0, …"*), and runs `AccountErasure.EraseAsync`, the code
self-service deletion uses. Erasure sets the seven benchmark foreign keys that point at the account to
null first (`BenchmarkRun.StartedByUserId`, `BenchmarkRunSeries.StartedByUserId`,
`BenchmarkRunGroup.CreatedByUserId`, `BenchmarkGroupAnalysis.ComputedByUserId`,
`BenchmarkBattery.CreatedByUserId`, `BenchmarkBatteryRun.StartedByUserId`,
`BenchmarkBatteryAnalysis.ComputedByUserId`), so the benchmark history stays and the user row can go.

Erasure is not one transaction across the database and the files. If it throws, a second audit row
*"Deletion failed: {message}"* is written and the call answers **500 `erasure-failed`**; the dialog
keeps the message and can be retried, since every erasure step is safe to repeat.

The dialog asks for one checkbox per important category with a count above zero (*"I understand 12
game logs and their top-score entries are erased."*), a reason, and the user name typed; **Delete
Account** stays unavailable until all are in. **Ban instead** closes it and opens the ban dialog.

## 5. Bulk Actions

A four-step full-screen wizard, *1. Users · 2. Action · 3. Review · 4. Results*, over
`AdminBulkUserOperationService` and the background runner `AdminBulkOperationRunner`. Closing it before
the run changes nothing; a running operation continues after the wizard closes and is followed in the
admin log.

1. **Users**: a preset (*Unconfirmed accounts older than 30 days*, *Locked out*, *Banned*, *Custom*)
   and the Users tab's filter bar; a count of matches and the first ten.
2. **Action**: one of six, each with its danger level.
3. **Review**: a server-side dry run bound to a short-lived preview, described below.
4. **Results**: progress while running, then the outcome of every account, the CSV report and *Show in
   admin log*.

| Action | Danger | Acknowledgments | Options |
|---|---|---|---|
| Delete accounts | `danger` | `reviewed`, `irreversible`; plus `advanced` when the advanced accounts are included | reason |
| Lock out | `caution` | `reviewed` | `until`, reason |
| Ban | `caution` | `reviewed` | reason |
| Unlock, Unban, Sign out everywhere | `safe` | none | — |

### 5.1 Preview

`POST bulk/preview` takes `{ action, options: { until?, reason? }, criteria }`, with the criteria in the
JSON form of the Users tab's filters. It:

1. Validates the options: a reason (1–512) for delete, lock out and ban (400 `reason`); for lock out an
   `until` in the future and within `LockoutMaxDays` (400 `until`); malformed criteria are 400
   `criteria`.
2. For delete, requires a **narrowing criterion**: *Email not confirmed*, *Last sign-in* *Never
   recorded* or more than 30 days ago, or *Registered more than 30 days ago*. Otherwise 400
   `narrowing`: *"Bulk deletion needs at least one of: Email not confirmed, Last sign-in, Registered
   more than 30 days ago."* A missing erasure path is 409 `erasure-paths`.
3. Orders the matches oldest `CreatedUtc` first, then by id.
4. Classifies each match:
   - **Refused**, never part of the operation. Every action refuses the administrator's own account
     (`self`) and administrators (`administrator`). Delete also refuses, in this order: `hold`,
     `pending-request`, `game-data`, `overseer-data`, `recent-sign-in` (within
     `BulkDeleteRecentSignInDays`, 90) and `too-new` (registered within `BulkDeleteMinAccountAgeDays`,
     7, or with no registration date). These are hard protections; nothing overrides them, and such an
     account can be deleted only one at a time (§ 4).
   - **Advanced** (delete only): an eligible account whose email is confirmed. It is listed separately
     and runs only when the execute request includes it.
   - **Already done**: nothing to change (unlock of an account not locked out, ban of a banned one,
     unban of one not banned, lock out of one already locked out until then). Counted, not run.
   - **Eligible**: everything else.
5. Caps the operation at `BulkMaxUsers` (500) eligible and advanced accounts, oldest first; the rest are
   reported as `truncatedCount` (*"Only the 500 oldest are in this operation; run it again for the
   rest."*).
6. Stores one `AdminBulkOperations` row (`Previewed`, expiring after `BulkPreviewMinutes`, 15) with the
   counts and, per refusal reason, a count and up to 50 user names; and one `AdminBulkOperationItems`
   row per eligible and advanced account.

It returns the operation id and expiry, the danger level and required acknowledgments, the counts
(`matchedCount`, `eligibleCount`, `advancedCount`, `refusedCount`, `noOpCount`, `truncatedCount`,
`maxUsers`), the eligible and advanced accounts, the refused groups, for delete the data totals (zero by
construction, shown anyway) and `requiredPhrase`.

### 5.2 Execute

`POST bulk/{opId}/execute` takes `{ excludedUserIds[], includeAdvanced, acknowledgments[],
confirmationPhrase }` and checks, in order:

| Check | Refusal |
|---|---|
| The caller made the preview | 403 `actor` |
| The preview has not expired | 409 `expired` |
| It is still `Previewed` | 409 `not-previewed` |
| No other operation is queued, running or stopping | 409 `busy` |
| Every excluded id is one of its accounts | 400 `excluded` |
| Every required acknowledgment is present | 400 `acknowledgments` |
| At least one account is left | 400 `empty` |
| Delete: the phrase is exactly `DELETE n ACCOUNTS` (`DELETE 1 ACCOUNT` for one), `n` counted after the exclusions and the advanced choice | 400 `phrase` |

It then marks the excluded accounts (and the advanced ones unless included), sets the operation
`Queued` and hands it to the runner. In the wizard an expired review is previewed again and its
acknowledgments, phrase and advanced choice are cleared.

### 5.3 The run

The runner takes one operation at a time and one account at a time, each in a fresh DI scope:

- Before each account it checks for a cancel request.
- It loads the account fresh and re-checks **both** the operation's criteria and the hard protections.
  An account that no longer matches, is now protected, or now has a confirmed email without the
  advanced choice is **skipped** with *"Changed since the preview: …"*; one with nothing left to change
  is skipped with *"Already done: …"*. Nothing is acted on that the preview would not have allowed.
- It calls the same `AdminUserManagementService` method a single action uses, with the operation's id,
  so the audit row carries `BulkOperationId`. A bulk delete skips the per-account typed name and
  acknowledgments, which the operation's phrase and acknowledgments replace, but not the guard or the
  deletion refusals.
- It records each account's outcome (`Changed`, `Skipped`, `Failed`), statuses before and after, and
  message.
- `BulkConsecutiveFailureLimit` (5) failures in a row stop the run with *"Stopped after 5 consecutive
  failures."*; the operation is `Completed` with that stop message.

| Status | Meaning |
|---|---|
| `Previewed` | Created by a preview; can be executed until it expires |
| `Expired` | A preview past its expiry; set when it is next read |
| `Queued` | Executed, waiting for the runner |
| `Running` | The runner is on it |
| `CancelRequested` | Stopping after the current account |
| `Canceled` | Stopped by a cancel, or a preview discarded |
| `Completed` | Every account processed, or stopped by the failure limit |
| `Interrupted` | Overseer stopped while it was queued or running; processed accounts keep their outcomes |

`POST bulk/{opId}/cancel` discards a preview (`Canceled`), asks a queued or running operation to stop
after its current account, and answers 409 `finished` for one that has ended. At startup every queued
or running operation becomes `Interrupted` and a requested cancel `Canceled`.

`GET bulk/{opId}?outcome=pending|changed|skipped|failed|excluded&skip&take` returns the status, the
counts and a page of accounts (`take` 1–200, default 50); the wizard polls it. `GET bulk?skip&take`
lists operations newest first, leaving out previews that were never run unless
`includePreviews=true`. `GET bulk/{opId}/report.csv` is the results report, UTF-8 with a BOM, one line
per account, the excluded ones included (`Operation, Action, UserName, UserId, Outcome, Excluded,
Advanced, Before, After, Message, ProcessedUtc`); a field starting with `=`, `+`, `-`, `@`, a tab or a
carriage return is prefixed with `'` so a spreadsheet does not run it as a formula.

## 6. The admin log

`AdminUserActions` is the durable record of every administrator action on an account:

| Column | Notes |
|---|---|
| `OccurredUtc` | |
| `ActorUserId`, `ActorUserName` | The administrator |
| `SubjectUserId`, `SubjectUserName` | The account; **no foreign key**, so the row outlives a deletion and keeps the user name. The email is never stored |
| `Kind` | `AdminUserActionKind` |
| `Reason` | Up to 512 characters |
| `Detail` | Up to 1,024: *"Before: In good standing. After: Locked out until 2026-10-20 15:00 UTC."*, or a deletion's counts |
| `BulkOperationId` | The bulk operation, foreign key to `AdminBulkOperations` with `SET NULL` |

The kinds are a stored contract, only ever appended to:

| Value | Kind | Written by |
|---|---|---|
| 1 | `LockedOut` | Lock out, single or bulk |
| 2 | `Unlocked` | Unlock |
| 3 | `Banned` | Ban |
| 4 | `Unbanned` | Unban |
| 5 | `GameRestrictionsChanged` | Game upload restrictions |
| 6 | `SignedOutEverywhere` | Sign out everywhere |
| 7 | `EmailConfirmed` | Mark email confirmed |
| 8 | `TwoFactorReset` | Reset two-factor |
| 9 | `Deleted` | Delete, single or bulk; a second row *"Deletion failed: …"* when erasure throws |
| 10 | `AiChatBlocked` | `OverseerAccessService` |
| 11 | `AiChatUnblocked` | `OverseerAccessService` |
| 12 | `HoldPlaced` | `AccountDeletionReviewService` |
| 13 | `HoldReleased` | `AccountDeletionReviewService` |
| 14 | `DeletionRequestApproved` | *Deletion requests* → Approve and delete |
| 15 | `DeletionRequestDeclined` | *Deletion requests* → Decline |
| 16 | `EmailChanged` | Change email |

A user deleting their own account on the website writes no row: no administrator acted.

**Retention.** Nothing prunes `AdminUserActions`, `AdminBulkOperations` or `AdminBulkOperationItems`
(whose `UserId` has no foreign key either), and account erasure does not touch them: they are kept
indefinitely. The per-user `SecurityEvent` rows of the same actions, kinds 45–52, cascade with the
account and are pruned after `ChatRetentionSettings:PruneSecurityEventDays` like every other event.

**The Admin log sub-tab** lists the entries newest first in batches of 20. Each card shows the action
as a word and its time, the account, *by* the administrator, the reason, the before → after, and for a
bulk row *Part of bulk operation #12* with **Open results**, which reopens the wizard on that
operation's results. It searches the account and administrator names, the reason and the detail, has
an **Action** facet, and shows *Operation* and *Account* chips when a link preset them: the wizard's
*Show in admin log* opens it filtered to the operation. Its filter is kept while the Users tab is open.

`GET log?search&kinds&actor&subjectUserId&bulkOperationId&skip&take` returns `{ totalCount, rows }`;
`kinds` is comma-separated kind names (unknown names are ignored), `actor` is the administrator's id or
part of the name, and `take` is 1–100 (50 when absent).

## 7. Sign-in rules

The website (MobileGnollHackLogger) and Overseer sign in against the same accounts. Both register
`GnollHackSignInManager` and read the same `IdentityLockout` section, which must stay identical in both
`appsettings.json` files.

### 7.1 Website and Overseer sign-in

`GnollHackSignInManager.CanSignInAsync` refuses a banned account, so Identity reports *not allowed*
for password, two-factor, recovery-code and external sign-ins and for `CheckPasswordSignInAsync`,
before it checks the password. Identity also refuses an unconfirmed account that way, since both apps
require a confirmed account. On such a refusal both sign-in pages check, in order:

1. **A locked-out account is refused first**, before any password check: the website shows its Lockout
   page, Overseer answers 401 *"This account is temporarily locked after too many failed sign-in
   attempts. Try again later."*.
2. **A wrong password counts toward Identity lockout** (`AccessFailedAsync`) like any other wrong
   password, and gets the usual *"Invalid login attempt."* / *"Invalid credentials."*.
3. **Only the correct password reveals the state.** A banned account gets *"This account has been
   banned."*, followed on the website by *"Contact {ReportEmailAddress} if you think this is a
   mistake."* (or *"Contact the GnollHack team if you think this is a mistake."* while that value is
   empty); Overseer answers 401 `{ message, isBanned: true }`. An unconfirmed account gets, in Overseer
   only, 401 *"This account is not permitted to sign in. Confirm your e-mail address and try again."*;
   the website keeps *"Invalid login attempt."*.

**The ban reason is never shown to the user**, on any surface.

Identity lockout is unchanged: `IdentityLockout:MaxFailedAccessAttempts` (10) consecutive failures lock
the account for `LockoutMinutes` (15). **Existing sessions end within
`IdentityLockout:SecurityStampValidationMinutes` (5)** of a ban, a lockout, *Sign out everywhere* or a
two-factor reset: both apps set `SecurityStampValidatorOptions.ValidationInterval` from it, at the cost
of one user lookup per signed-in user per interval. Overseer's chat hub applies the same interval to an
open chat connection, which ends no later than its first hub call once that interval has passed.

### 7.2 GnollHack: game APIs and `/api/session/create`

The game APIs (`/xlogfile`, `/bones`, `/api/savefiletracking/*`, `/api/replay`, through
`GameApiAuthenticator`) and Overseer's `/api/session/create`, which GnollHack's Overseer page calls with
the same stored credentials, check the password on every request with
`CheckPasswordSignInAsync(..., lockoutOnFailure: false)`. **They never count toward Identity lockout**,
so a client retrying a mistyped password can never lock the account; a locked-out account is still
refused. Their failures count toward the in-memory `GameApiSignInThrottle` instead:

| Limit | Key | Failures | Window | Block |
|---|---|---|---|---|
| Pair | account and address | 10 | 15 minutes | 15 minutes |
| Account | account, across addresses | 50 | 60 minutes | 30 minutes |
| Address | address, across accounts | 100 | 15 minutes | 15 minutes |

- The throttle is checked **before** the account is looked up, so a throttled request costs no lookup
  and no password hash. A refused request is not counted and extends no block.
- The account key is the upper-cased user name, whether or not the account exists. An unknown user and
  a wrong password count; a correct password clears the pair's and the account's failures, not the
  address's.
- The tight limit is per pair, so another address guessing a player's password cannot keep the player
  out: it meets the account limit only after 50 failures an hour.
- It is **per process**: not shared between the website and Overseer, or between instances, and reset
  on restart. At most 100,000 keys are kept per limit, the least recently used dropped first.
- The limits are the `GameApiSecurity` section of each app's `appsettings.json`
  (`PairFailureLimit`, `PairWindowMinutes`, `PairBlockMinutes`, `AccountFailureLimit`,
  `AccountWindowMinutes`, `AccountBlockMinutes`, `IpFailureLimit`, `IpWindowMinutes`, `IpBlockMinutes`,
  `PerIpLimitsEnabled`).

`GameApiSignInClassifier` decides each outcome; a banned or unconfirmed account is named only after the
correct password, and is never counted:

| Password check | Outcome | Counts toward the throttle |
|---|---|---|
| Succeeded | Success | no |
| Locked out | Locked out | no |
| Not allowed, password correct, banned | Banned | no |
| Not allowed, password correct, unconfirmed | Email not confirmed | no |
| Anything else (wrong password, unknown user, not allowed with a wrong password) | Failed | yes |

**Game API responses** (`text/plain; charset=utf-8`; every refusal a player can cause is a 403, the
status every GnollHack client stops retrying on):

| Outcome | Status | Body | `Retry-After` |
|---|---|---|---|
| Failed | 403 | *"Sign-in failed."* | — |
| Throttled | 403 | *"Too many failed sign-in attempts. Try again in {n} minutes."* | yes |
| Locked out, up to 24 hours | 403 | *"This account is temporarily locked. Try again in {n} minutes."* | yes |
| Locked out, beyond 24 hours | 403 | *"This account is locked until {yyyy-MM-dd HH:mm} UTC."* | yes, clamped to 366 days |
| Banned | 403 | *"This account has been banned."* | — |
| Email not confirmed | 403 | *"Confirm your email address before signing in to GnollHack online features."* | — |
| Two-factor account | 412 | *"This account requires two-factor authentication."* | — |
| Antiforgery mismatch | 401 | *"AntiForgeryToken is invalid."* | — |

The controllers' own 423 checks for game log and bones restrictions stay after sign-in.

**`/api/session/create` refusals** are 401 `{ code, message }`, the status GnollHack's Overseer page
stops retrying on; the message is the game API body above, or *"Invalid credentials."*:

| `code` | When |
|---|---|
| `throttled` | The throttle refused the request |
| `locked_out` | The account is locked out |
| `banned` | Banned, with the correct password |
| `email_not_confirmed` | Unconfirmed, with the correct password |
| `invalid_credentials` | Wrong password or unknown user |

A wrong antiforgery secret is a 401 with no body and a blank user name or password a 400. After the
password passes, an account blocked from the AI chat gets the 403 of
[`security-monitoring.md`](security-monitoring.md) § 3.

### 7.3 Two limits of the throttle

- **An admin Unlock does not clear the game throttle.** The website's throttle lives in the website's
  process, out of Overseer's reach. A pair block lasts at most 15 minutes and an account block 30, so a
  player unlocked by an admin can be refused by GnollHack for up to that long.
- **Neither app uses forwarded headers.** Behind a reverse proxy every client has the proxy's address:
  the pair limit then behaves like a per-account limit, and the address limit could block every player
  at once. Set `GameApiSecurity:PerIpLimitsEnabled` to `false` there; the deployment check in
  [`security-monitoring.md`](security-monitoring.md) § 8 says how to tell.

## 8. Configuration

| Key (Overseer `appsettings.json`) | Default | Meaning |
|---|---|---|
| `AdminUserManagement:BulkMaxUsers` | 500 | The most accounts one bulk operation takes, oldest first |
| `AdminUserManagement:BulkDeleteMinAccountAgeDays` | 7 | Bulk delete refuses an account registered within this many days |
| `AdminUserManagement:BulkDeleteRecentSignInDays` | 90 | Bulk delete refuses an account that signed in within this many days |
| `AdminUserManagement:BulkPreviewMinutes` | 15 | How long a preview can be executed |
| `AdminUserManagement:LockoutMaxDays` | 365 | The furthest ahead an admin lockout can end |
| `AdminUserManagement:BulkConsecutiveFailureLimit` | 5 | A bulk run stops after this many failures in a row |
| `AdminUserManagement:AccountSiteBaseUrl` | `https://account.gnollhack.com` | The account website the *Change email* instructions link to |
| `IdentityLockout:SecurityStampValidationMinutes` (both apps) | 5 | How soon signed-in sessions notice a new security stamp |
| `GameApiSecurity:*` (both apps) | § 7.2 | The game sign-in throttle |

## 9. Before deploying

Accounts banned before this change had `IsBanned` set but could still sign in to the website and
Overseer; they now cannot, and the list shows them as *Banned* with no date or reason. Count them first
(the `database-queries` skill has the procedure):

```sql
SELECT COUNT(*) FROM AspNetUsers WHERE IsBanned = 1;
```

Remember § 2.1: until accounts have signed in after deployment, *Last sign-in* reads *Never recorded*.

## 10. Tests

In `Overseer.Tests/UnitTests/`: `AdminUserStatusTests` (the two rule implementations agree),
`AdminUserManagementServiceTests` (guards, validation, audit rows and events, the deletion refusals and
the audit row of a failed erasure, and change email: the user name kept, the instructions email, a failed
or unconfigured sender, a taken or current address), `AdminEmailChangeEmailBuilderTests` (the
instructions email's link, wording and encoding), `AdminBulkUserOperationTests` (preview classification and cap,
execute refusals, the advanced choice, the re-check, cancel, the failure limit and interrupted
operations), `AccountErasureTests` (the benchmark foreign keys), `GnollHackSignInManagerTests`,
`GameApiSignInClassifierTests`, `GameApiSignInThrottleTests`, `GameApiSignInResponsesTests` and
`SessionCreateLockoutTests` (no Identity lockout after repeated wrong passwords, the throttle by
address, a banned account with the correct password). `IntegrationTests/AdminSecurityTests` checks 401
and 403 on every `api/admin/users` route. No test project hosts the website, so its sign-in page and the
game API controllers are checked manually.
