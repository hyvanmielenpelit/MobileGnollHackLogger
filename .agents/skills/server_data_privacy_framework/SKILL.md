---
name: server_data_privacy_framework
description: >-
  Map and traps for Overseer's data privacy framework — the layer that decides what leaves the
  server, what is stored in clear and what is not. Read before touching confidential sessions,
  ephemeral (incognito) sessions, envelope encryption or crypto-shredding, outbound DLP masking,
  attachment validation or malware scanning, document parsing and RAG, the untrusted-content
  boundary, the provider confidentiality posture ladder, the custom-endpoint SSRF guard, the
  security headers and CSP, or the telemetry suppression that hangs off all of them. Answers
  "why is this decrypted into a local and that one in place", "where is content actually
  encrypted", "why does the validator classify three ways", "why does a floor parse its own
  booleans", "why is a session reference a type", "what does an ephemeral session refuse to
  write", and "which claim am I about to overstate". The authoritative description of what
  exists is docs/overseer/data-privacy-framework.md; this skill is the orientation map, the
  mistakes already made and fixed, and the honesty rules a change must not quietly lower.
---

# Data Privacy Framework: The Map, and the Traps

---

## 1. What this skill is, and what it is not

**It is not a summary of the framework.** [`docs/overseer/data-privacy-framework.md`](../../../docs/overseer/data-privacy-framework.md)
is the authority on *what exists* — the four tiers, every control as built, the residual risks —
and it is long because it earns it. Read it when you need to know what a control does.

**This skill is the map and the traps.** Where each piece lives, and the specific mistakes a
competent implementer already made on the first attempt and had to undo. The record of *why* each
decision went the way it did is in the nine walkthroughs at
`<plans-root>/hyvanmielenpelit/MobileGnollHackLogger/2026-09-09/data_privacy_master_plan/` —
`walkthrough.md` and `walkthrough_A.md` … `walkthrough_H.md`, one per stage A through I. When a
comment in the code says "this is deliberate", the walkthrough for that stage says what happened
when it was not.

---

## 2. Where things live

| Namespace / path | Owns | Why there |
|---|---|---|
| `Overseer/Services/Privacy/` | The keyring, `ContentProtectionService`, `AttachmentValidator`, the three malware scanners plus the composite, `ConfidentialPolicyResolver`, `ConfidentialityPostureService`, `EndpointPolicy`, `EphemeralSessionStore`, `SessionRef`, `UntrustedContentWrapper`, `ConfidentialExecutionScope` | Everything that is a policy decision or an envelope, in one place, so a new control is discoverable |
| `Overseer/Services/Privacy/Dlp/` | `DlpScannerService` (patterns, gates, `DlpPolicy`), `DlpTokenVault`, `DlpStreamUnmasker` | Detection, the per-turn mapping, and the stream reader are one subsystem; nothing else may mint a token |
| `Overseer/Services/Documents/` | `DocumentParserService`, `ParsedDocument` | Bytes to text. Decides format from the bytes, never throws |
| `Overseer/Services/Rag/` | `DocumentChunker`, `DocumentRagService`, `IEmbeddingService` / `LocalOnnxEmbeddingService`, `RagSidecarStore` | Chunking, ranking and the on-disk sidecar, which is content and treated as such |
| `Overseer/Middleware/SecurityHeadersMiddleware.cs` | CSP and the four other response headers | Registered **before** `UseStaticFiles` in `Program.cs`, so it covers static assets and the SPA fallback — therefore also before `UseRouting`, so `GetEndpoint()` is null and the policy cannot vary per endpoint |
| `Overseer/Services/ChatService.cs` | The **only** encryption write site, two of the three masking sites, every decrypt read path, the ephemeral write sink | One turn, one choke point (§ 3.2) |
| `Overseer/Services/Agents/AgentLoopRunner.cs` | The third masking site, on tool results re-entering `messageHistory`. **Never sees a key** | § 3.2 |
| `Overseer/Hubs/ChatHub.cs`, `Overseer/Controllers/ChatController.cs` | Parse a `SessionRef` at the boundary and authorise through one helper | § 3.5 |
| `GnollHackServer.Data/Privacy/CryptoShred.cs` | Nulling a session's wrapped content key, and a user's API-key material | See below |

**Why `GnollHackServer.Data/Privacy/` exists at all.** `MobileGnollHackLogger` and `Overseer`
each reference only `GnollHackServer.Data`, and **neither references the other**. Account
deletion is a Razor page in `MobileGnollHackLogger`; `ChatRetentionService` lives in `Overseer`
and is simply unreachable from it. So **"route it through the service" is not implementable
across that boundary** — anything both web projects need is a static helper in the data project
or it does not exist. Lifting the retention service down instead is worse: it depends on
`IConfiguration`, `ILogger`, disk paths and settings, none of which belong in an entity assembly.

---

## 3. The traps

Each of these was a real defect, most of them found after the code was written and reviewed.

### 3.1 Decrypting replayed history in place

`pastMessages` in `ChatService.StreamMessageAsync` is loaded **tracked**. Assigning the plaintext
back onto `pm.Content` makes the `SaveChangesAsync` at the end of the turn write **plaintext into
a confidential session** — inverting the feature, silently, and only for the sessions that asked
for protection.

**Decrypt into a local.** `ConfidentialEncryptionRegressionTests` runs that exact sequence and
asserts the stored value is still an envelope; it is the only test that catches this.

The tool-call rows **are** decrypted in place, and that is safe **there and only there**:
`pastToolCalls` is an `AsNoTracking` projection into detached instances, so nothing is written
back. Without the decrypt, `ToolCallHistoryDigest.Build` would summarise base64. If you ever make
that query tracked, this becomes the same bug.

### 3.2 `AgentLoopRunner` looks like the write site and is not

It constructs every `ChatMessageToolCall` and assigns `ArgsText`, `Result` and `Error`. An
implementer who greps for the writer lands there. But those rows reach the database only as
`asstMsg.ToolCalls`, at **one choke point in `ChatService`** immediately before the assistant
message persists — which is why one pass over them is sufficient.

Pushing the encryptor into the loop would spread key material across it **and break DLP masking**,
which has to run on plaintext history: a loop holding ciphertext cannot feed `messageHistory`. The
constraint is load-bearing in three directions now, because the ephemeral write sink sits at the
same point. Read the choke-point comment before touching either.

### 3.3 Masking is an egress control, not a storage one

`ChatMessage.Content` and `ChatMessageToolCall.Result` keep the **unmasked** text by design — the
row is the user's own record of their own message, and in a confidential session § 3.2's choke
point encrypts it. Only the copy that leaves the process is masked.

**Which is exactly why masking must run over replayed history**, not only the new message. Turn
two replays the stored plaintext; masking only what the user just typed sends the secret in clear
on every turn after the first — a leak that *appears* to be fixed. There are three sites: the
history loop (after the tool-call digest is appended, so summarised arguments go in the same
pass), the new message together with every uploaded document's wrapped text, and each tool result
in `AgentLoopRunner`. Removing any one of them reopens the same hole by a different route.

Tool **arguments** are never unmasked. For a local tool that is a quality loss; for
`ToolCategory.ExternalLookup` unmasking would send the secret to a third party.

### 3.4 The DLP vault deduplicates per secret, and the unmaskers cannot be lazy

`DlpTokenVault` maps one secret to **one** token however many times it occurs, across every
`Mask` call in the turn. Numbering per occurrence would show the model `[REDACTED_API_KEY_1]` and
`[REDACTED_API_KEY_2]` for the same string, and it would reason about them as **two different
credentials** — "the first key is invalid, try the second". That is worse than not masking, which
is why deduplication is the class's defining property and not an optimisation. It is keyed on the
secret alone, ignoring class, and guarded by a lock because parallel tool calls mask concurrently.

Two things about `DlpStreamUnmasker`:

- **`Flush()` is not optional.** At the end of a turn each sliding window still holds whatever
  trailing characters could still have become a token. Without the flush **every reply loses its
  ending — and only when it happens to end on one of those characters.** There is a test that ends
  a reply mid-window for exactly that reason.
- **The unmaskers cannot be created lazily.** Building them only when the vault is already
  non-empty is wrong: the agent loop masks tool results *as it runs*, so the vault can gain its
  first entry after the prompt was assembled. A turn whose message was clean but whose tool result
  carried a key would then echo the placeholder straight to the user. Create them whenever masking
  is enabled at all, and note `MaxTokenLength` is a computed **bound**, not the longest token
  issued so far, for the same reason.

There are **two** unmaskers per turn — `chunk` and `thinking_chunk` are separate streams the client
renders in different places, so one window would hold back a fragment of one and splice it into
the other.

### 3.5 A session reference is a type, not a number

`SessionRef` is a readonly struct with two states, `Persistent(long)` and `Ephemeral(Guid)`.
The design it replaced identified an ephemeral session by `sessionId < 0`, checked independently
in the hub, the controller, the chat service and the ongoing-generation manager. Every one of
those checks is invisible to the compiler, and a missed one is **an EF query against a negative
primary key — which returns no row, so the failure is silence.** Retyping the three surfaces
produced fourteen compile errors in unrelated code.

Anything that queries `ChatSession` by a reference must say which case it means: `IsPersistent`
then `PersistentId`, whose accessor throws rather than returning 0. `default(SessionRef)` is
neither state and reports itself invalid. The parser refuses `-1`, `0`, `eph_` with no GUID and
the all-zero GUID — the old sentinel is rejected, not merely unused.

> **`ChatHub` is where this bites.** Four of its five methods authorised by a `ChatSession` row
> lookup, which an ephemeral session has **none of by construction**: the lookup returns null,
> `Groups.AddToGroupAsync` is never reached, and the client receives no tokens, no tool events and
> no completion — **a hung model, not a refused connection.** All four now go through one
> `IsOwnedByCallerAsync` helper, so a new hub method cannot reintroduce it by omission. Add hub
> methods through that helper.
>
> `LeaveSession` is the deliberate exception: it authorises nothing and reads no user id, because
> removing your own connection from a group needs no permission. What it does need is the **parse**,
> or the group name will not match the one `JoinSession` added.

The wire format is unchanged for everything that existed: a persistent reference serialises as its
bare decimal id, so its SignalR group name is byte-identical. `ChatEvent.SessionId` is a string,
and the client compares it as one — the stale-event guard used to be
`typeof evt.sessionId === 'number'`, which would have stopped matching silently.

### 3.6 An ephemeral session writes no row and no file

No `ChatSession`, `ChatMessage`, `ChatMessageToolCall` or `ChatMessageAttachment` row, and no file
in `ConversationsDataLocation`. Nothing is added to the `DbContext` and `SaveChangesAsync` is
never called on that path — "zero rows" is a property of the code, not a promise about it.

**Including no RAG sidecar, because a sidecar is a file.** `RagSidecarStore.TryWriteAsync` returns
null for an ephemeral session as the *same* rule, not as an exception to it. Anything new that
writes to disk during a turn inherits this obligation.

Two things that are *not* exceptions and look like them: an ephemeral turn still records operator
quota usage (`RecordUsageAsync` names no session and no message, and incognito must not be a way
to spend the operator's budget uncounted); and the attachment path deliberately **drops** the
persisted path's `ConversationsDataLocation` precondition, because gating on a storage location an
ephemeral session never uses would silently discard every incognito attachment on a deployment
that has not configured one.

Ephemeral buffers are **plaintext and deliberately not encrypted** — there is no rest to protect,
and the DEK would sit in the same memory as the plaintext. Content is held as UTF-8 **bytes rather
than strings** so `CryptographicOperations.ZeroMemory` can actually overwrite it; the CLR offers no
way to overwrite a `string`.

### 3.7 The attachment validator classifies three ways, not two

**Image**, **binary document**, or **text**, each checked against its own rule: magic bytes
agreeing with the declared image type; `%PDF-` or `PK\x03\x04` for a type named in
`PrivacySettings:Attachments:BinaryDocumentContentTypes`; the control-byte scan for everything
else. Treating every non-image as text is what **refused every PDF** — correctly, since a PDF is
binary, for entirely the wrong reason.

"Binary" never means "unchecked". And a document **declared as text is refused, not
re-classified**: the declared type is what the rest of the pipeline routes on, so quietly
promoting a mislabelled upload would let a client choose which parser runs against its bytes.

### 3.8 Three places enumerate accepted extensions on the client

`chat.component.ts` carries `attachmentAcceptExtensions`, served by `GET /api/settings` from the
same allowlist `AttachmentValidator` enforces; the literal survives only as the fallback for a
failed settings call. **Three things read it**: the picker's `accept` attribute, `addFiles()` and
`receiveNativeFiles()`. Each of the latter two once held its own hardcoded array — so a widened
`accept` offered a `.pdf` that the composer then **discarded with no message at all**, the fix
silently undone one layer down. All three must read the served list; the two nobody looks at are
the two that matter.

### 3.9 Every privacy floor parses its own booleans

`ConfigurationBinder.GetValue<T>` **throws** on a value it cannot convert. `ConfidentialPolicyResolver`,
`EndpointPolicy` and `DlpScannerService` are DI singletons, so `"DisableToolEgress": "yes"` in
`appsettings.json` used to fail the application at startup **from inside a constructor, with an
error naming dependency injection rather than the setting**.

All three now read with `bool.TryParse(section[key]?.Trim(), …)` and fall back to the **closed or
strict end**. A typo can then neither weaken a floor, open the SSRF surface, nor stop the
application. Match that pattern in any new floor; do not reach for `GetValue<T>` because it is
shorter. This is the same principle as the rest of the framework: a malformed keyring is a startup
alert, an unparseable posture resolves down, an unrecognised persistence name resolves to the
default.

### 3.10 Widening a column removes whatever was enforcing the plaintext bound

`ChatSession.Title` and `ChatMessageAttachment.FileName` went 256 → 2048 to hold an envelope. The
257th character of a title used to fail **because the column rejected it** — the rename endpoint
validates for empty and for illegal characters and nothing else. At 2048 a normal session could
hold a 2048-character title, which on a later upgrade needs 10,973 characters: the widening would
have caused exactly the overflow it exists to prevent, one ceiling higher.

So the cap moved into the application — `ChatController.MaxPlaintextTitleLength` and
`ChatService.MaxPlaintextTitleLength`, both 256, enforced in `UpdateSessionTitle` *and* in
`GenerateTitleAsync`, with `MaxFileNameLength` doing the same job in `AttachmentValidator`. **Any
future widening asks the same question: what was the old width silently enforcing?**

Related: base64 legitimately contains `/`, so the rename endpoint's illegal-character check runs
on the plaintext only. Nothing may re-validate a *stored* value against it.

### 3.11 A crypto-shredded session is a normal outcome, not a programming error

A session whose wrapped key has been nulled is the expected result of a partial deletion —
`PermanentlyPurgeSessionsAsync` shreds **before** deleting anything, precisely so an interruption
leaves unreadable content rather than readable content. Reads therefore return
`ContentProtectionService.UnreadableNotice` (and empty bytes for a file), never throw and never
return an empty string, which would look like the model having said nothing. `Decrypt` used to
reach `UnwrapDek`, find no key columns and throw an `InvalidOperationException` that surfaced as a
500.

The same notice covers a corrupt row and a retired key version, which is what makes the rotation
runbook's "retire `v1` only when no session names it" a visible failure rather than a silent one.

---

## 4. Testing notes

Run the suite the way `.agents/AGENTS.md` specifies, and the filter is **not optional**:

```bash
dotnet test Overseer.Tests --filter-not-trait "Category=UsesExternalApi"
```

Without it the run calls the live OpenAI, Anthropic and Google APIs, spending quota and money.
The filter **fails open**, so a typo in the trait name excludes nothing and says nothing — verify
a changed filter by discovery (`--list-tests --filter-trait "Category=UsesExternalApi"`), never by
running the suite unfiltered. The rest of the rules are in `testing_guidelines`.

Four harness facts this framework's own tests were built on:

- **The EF in-memory provider cannot translate `ExecuteUpdate` / `ExecuteUpdateAsync` or
  `ExecuteDeleteAsync`, and it ignores foreign keys.** `CryptoShred` is deliberately set-based, so
  proving what its statement *does* needs a relational provider: `ConfidentialEncryptionRegressionTests`
  and `RagSidecarLifecycleTests` open an in-memory **SQLite** connection for exactly those tests.
  A relational provider then enforces the foreign keys the in-memory one was ignoring — a test
  that passes on in-memory can be storing rows the real database would reject.
  `ChatRetentionService.PermanentlyPurgeSessionsAsync` is `virtual` instead, so a test can record
  which sessions a path routed to it without running the bulk SQL.
- **`AddDbContext(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()))` invokes that lambda per
  DbContext construction**, so every scope gets its own database and a turn finds none of the
  seeded rows. **Capture the name outside the lambda.** Files that build a
  `DbContextOptionsBuilder` directly are unaffected.
- **`Assert.Contains` / `Assert.DoesNotContain` on strings default to a culture-sensitive
  comparison**, under which U+FEFF has zero collation weight — so "mark + body" and "body" compare
  *equal* and the assertion can neither pass nor fail for the right reason. Any assertion about a
  zero-width or combining character needs `StringComparison.Ordinal`; see
  `DocumentIngestionTurnTests`.
- **Every mini DI container used by a `ChatService` test must register the whole Privacy service
  set** — the validator, the scanner, `ContentProtectionService`, the keyring, the ephemeral store,
  the DLP scanner, the parser and the RAG services. Adding a service to `ChatService`'s constructor
  breaks several test files at once, and `ChatServiceTests` sat unrunnable for several stages
  because it carries `UsesExternalApi` and the default run never reached it.

---

## 5. The honesty rules

The framework's standard for its own claims, and it is not decoration — each of these was written
after choosing the weaker, truer statement over the stronger one. **A change that makes one of
them less true changes the framework doc and the UI copy with it, in the same commit.**

- **PDF active content is reported, not neutralised.** PdfPig executes nothing, so `/JavaScript`,
  `/OpenAction`, `/AA` and `/Launch` were never going to run. The value is telling the user their
  file contained them.
- **DLP masking reduces accidental credential egress; it does not guarantee it.** A secret with no
  recognisable shape passes straight through. This is stated *first* wherever masking is described,
  not last.
- **RAM is not a legal or forensic boundary.** "Not saved" means "not written to Overseer's
  database or its file store" and nothing stronger; process memory can be paged out or captured in
  a dump. And an ephemeral conversation **still reaches the AI provider**. Both halves are in the
  product where the mode is chosen, not in a tooltip.
- **Crypto-shredding does not reach a backup taken before the shred.** That backup holds the
  wrapped key and the master key is still in configuration. This is the single most tempting
  overstatement in the whole framework.
- **A posture is a claim about a contract, not a technical control.** `ZeroRetention` means an
  administrator read an agreement and dated it. A **self-declared** posture on a user's own key can
  never yield a green badge, and `ResolveForUserKey` returns `IsOperatorVerified: false`
  unconditionally so no type can express otherwise.
- **Retrieval reduces exposure; it does not eliminate it**, and an assistant answering from six
  chunks of a ninety-page PDF must not look like one that read all ninety. Both the model (on the
  wrapper element) and the user (through the `attachment_excerpt` notice) are told.
- **Prompt injection is bounded, not prevented.** `UntrustedContentWrapper` removes the
  *structural* attack; a persuasive injection inside a correctly wrapped document still works.

Two directions that look inconsistent and are not: an unrecognised **posture** resolves *down*
(failing upward would over-promise), while an unparseable **`Persistence`** resolves to `Encrypted`
(failing downward would silently produce a plaintext confidential session). Each fails toward the
safer outcome for its own question.

---

## 6. Cross-references

- [`docs/overseer/data-privacy-framework.md`](../../../docs/overseer/data-privacy-framework.md) —
  the authority on **what exists**; this skill is the map and the traps
- [`chat_retention_architecture`](../chat_retention_architecture/SKILL.md) § 3a — confidential
  retention, the two materialised scalars, and the rule that a fifth deletion path must route
  through `PartitionAndDeleteAsync`
- [`sentry_logging_architecture`](../sentry_logging_architecture/SKILL.md) — `AuthSentryEventProcessor`,
  whose `httpContext != null && …IsAuthenticated != true` guard shape is what forced
  `ConfidentialExecutionScope` to be an `AsyncLocal`
- [`tool_execution_architecture`](../tool_execution_architecture/SKILL.md) — the batching and
  execution layer the egress lockout's second guard sits in
- [`configuration_management`](../configuration_management/SKILL.md) — the
  `appsettings.json`-versus-User-Secrets split; key material is User Secrets only, paths are
  settings
- [`testing_guidelines`](../testing_guidelines/SKILL.md) — the trait convention and why the filter
  is mandatory
- [`server_implementation_planning`](../server_implementation_planning/SKILL.md) — this framework's
  stages were nine planned rounds, and a change to it is a planned change
