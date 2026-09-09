---
name: testing_guidelines
description: Guidelines for implementing and running tests, especially those interacting with external AI APIs.
---

# Testing Guidelines for Overseer

When writing or executing tests in the Overseer project, you MUST adhere to the following guidelines regarding external API consumption and integration testing conventions.

## 1. AI API Quota Protection

Tests that call external APIs (like OpenAI, Anthropic, or Google) consume quota and can cost money.

*   **Ask for Permission**: You MUST ALWAYS ask the user for explicit permission before running any test that hits an external AI API.
*   **Trait Tagging**: Every test method or class that connects to an external API must be decorated with `[Trait("Category", "UsesExternalApi")]`.
*   **Default CLI Test Command**: When running test commands in verification plans or automated testing routines, always append `--filter-not-trait "Category=UsesExternalApi"` to prevent unintended API calls and quota consumption. **This is the command for this repository's implementation plans and verification runs:**
    ```bash
    dotnet test Overseer.Tests --filter-not-trait "Category=UsesExternalApi"
    ```
    The whole-solution variant, for when the other test projects matter too:
    ```bash
    dotnet test MobileGnollHackLogger.slnx --filter-not-trait "Category=UsesExternalApi"
    ```
    The opt-in counterpart, for a deliberate live-API run, is the same argument without the `not`:
    ```bash
    dotnet test Overseer.Tests --filter-trait "Category=UsesExternalApi"
    ```
    Both commands are also stated in **`AGENTS.md`**, which is loaded into every context window, so they are available without invoking this skill. The Verification Plan rules that consume them are in `server_implementation_planning`.

### 1a. What makes that command work, what makes it unsafe, and why it is spelled this way

**The repository-root `global.json` is a prerequisite, not decoration.**

```json
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

`Overseer.Tests` references `xunit.v3` and `Microsoft.NET.Test.Sdk`, which make it a
**Microsoft.Testing.Platform (MTP) application**. From the .NET 10 SDK onward `dotnet test`
refuses to run an MTP application through the old VSTest target and fails with

```
error : Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on
.NET 10 SDK and later. If you use dotnet test, you should opt-in to the new dotnet test experience.
```

The `global.json` entry above is that opt-in. Delete the file and every `dotnet test` command in
this repository stops working; the error names no file, so the cause is not obvious from it.

> ⚠️ **`dotnet.config` is *not* the mechanism here, despite what current Microsoft documentation
> says.** A repository-root `dotnet.config` carrying `[dotnet.test.runner] name =
> "Microsoft.Testing.Platform"` was tried first on SDK **10.0.401** with
> `microsoft.testing.platform.msbuild` **2.3.3** and had **no effect** — `dotnet test` still routed
> to VSTest and still failed. The MSBuild target that raises the error gates on a property named
> `_SupportsGlobalJsonTestRunner`, which is the giveaway. Use `global.json` on this toolchain, and
> re-verify before switching: a change that silently reverts to the broken state looks like a
> tidy-up in review.

**The filter fails open.** This is the important one, because the filter is the only thing standing
between a routine test run and real money on a machine that has the live-test secrets configured.
A typo in either half matches nothing, therefore excludes nothing, and runs the live-API tests
silently. Measured on 2026-09-09 by discovery count:

| Filter | Tests selected |
|---|---|
| *(none)* | 1482 — the whole suite, live-API tests included |
| `--filter-not-trait "Category=UsesExternalApi"` | **1478** — correct |
| `--filter-trait "Category=UsesExternalApi"` | 4 — exactly the live-API tests |
| `--filter-not-trait "Categoy=UsesExternalApi"` *(name typo)* | 1482 — **fails open, no warning** |
| `--filter-not-trait "Category=UsesExternalApis"` *(value typo)* | 1482 — **fails open, no warning** |
| `--filter-not-trait "Category!=UsesExternalApi"` *(VSTest `!=` habit)* | 1482 — **fails open, no warning** |

The last row is the one to watch during the transition: `--filter-not-trait` takes a plain
`name=value` pair, and an operator smuggled into the value is accepted in silence.

**So verify a filter by discovery, never by running the suite.** `--list-tests` enumerates without
executing anything, so it costs nothing:

```bash
dotnet test Overseer.Tests --list-tests --filter-trait "Category=UsesExternalApi"
```

That must list exactly the live-API tests and nothing else. The arithmetic to check is
`unfiltered − live == filtered`, all three from discovery, and then that the filtered **execution**
total matches the filtered discovery total. As of 2026-09-09: 1482 − 4 = 1478, and a filtered run
executes 1478.

> **The four live tests fail rather than skip when their secrets are absent**
> (`LiveTestSecrets.DescribeMissing` feeds `Assert.True`), which is deliberate — see § 1's secrets
> note. The consequence for this section: on a machine **without** the secrets an unfiltered run
> costs nothing and merely reports four failures, while on a machine **with** them it spends real
> money. Do not infer from a clean unfiltered run on one machine that the filter is unnecessary on
> another.

**Why `--filter-not-trait` and not `--filter`.** Both select the same 1478 tests, in every command
form and under `--list-tests`, so this is not a correctness question. It is a longevity one:
Microsoft.Testing.Platform's own help describes `--filter` as *"Filter using the VSTest filter
syntax"* and links to VSTest documentation, which makes it a **compatibility shim**.
`--filter-not-trait` / `--filter-trait` are the native xunit v3 options. Since the reason this
section exists at all is that a VSTest compatibility path was removed in the .NET 10 SDK,
standardising the repository's most-repeated command on a second one would be the same bet twice.

The native pair is also harder to get dangerously wrong. The old pair was
`--filter "Category!=UsesExternalApi"` to exclude and `--filter "Category=UsesExternalApi"` to opt
in — **one character apart, with the shorter one running only the tests that cost money.** The
native pair differs by the word `not`.

> **The honest counter-argument, for whoever revisits this:** `--filter-not-trait` is an xunit v3
> *extension* option, native to the framework rather than to the platform. If this project ever
> moved off xunit v3, that option name would change, whereas VSTest filter syntax is the more
> cross-framework spelling. That is a real cost, and it was accepted because nothing suggests this
> repository is leaving xunit. `--filter "Category!=UsesExternalApi"` still works today, so an
> older plan or commit carrying it is not broken — just not the documented form.

### Package hygiene: three VSTest-era references that do nothing

`Overseer.Tests` is an MTP application because **`xunit.v3` makes it one** — `xunit.v3` 4.0.0 pulls
`xunit.v3.core.mtp-v2`, whose dependencies include `Microsoft.Testing.Platform` and
`Microsoft.Testing.Platform.MSBuild`. Nothing else in the project supplies that. Three references
are therefore VSTest-era leftovers rather than load-bearing parts of the test setup:

| Package | What it is | Status |
|---|---|---|
| `Microsoft.NET.Test.Sdk` 18.10.0 | pulls `Microsoft.TestPlatform.TestHost` and `ObjectModel` — the VSTest host | not used by the CLI path |
| `xunit.runner.visualstudio` 4.0.0 | the VSTest adapter | not used by the CLI path |
| `coverlet.collector` 10.0.1 | a VSTest data collector | referenced in no doc, script or workflow — default-template residue |

**Verified on 2026-09-09:** with all three removed, `dotnet test Overseer.Tests
--filter-not-trait "Category=UsesExternalApi"` ran **1478 passed, 0 failed**. They were then
restored, and the removal was **deliberately not applied**, for one reason:
`xunit.runner.visualstudio` is plausibly what lets Visual Studio Test Explorer discover these
tests, and that cannot be verified from a terminal. **Before removing them, open Test Explorer in
Visual Studio and confirm discovery still works** — VS supports MTP natively in recent versions,
but it may need enabling. If coverage is ever wanted, note that `coverlet.collector` is driven by
VSTest's `--collect:"XPlat Code Coverage"`; the MTP equivalent is a different package
(`Microsoft.Testing.Extensions.CodeCoverage`) and a `--coverage` switch.
*   **CLI Instructions in Code**: The test file must contain a human-readable header comment instructing developers and agents on how to skip these tests during normal execution.
    ```csharp
    // To run tests while SKIPPING this file (to save AI API quota), use:
    // dotnet test MobileGnollHackLogger.slnx --filter-not-trait "Category=UsesExternalApi"
    ```
*   **Test Secrets & Configuration**: Live API credentials must be stored in User Secrets (never committed). See **[`docs/overseer/test-configuration.md`](../../../docs/overseer/test-configuration.md)** for the complete schema, setup commands, and troubleshooting guide.

> [!NOTE]
> **Solution File Format**: The repository uses the modern Visual Studio solution format **`MobileGnollHackLogger.slnx`** (not `.sln`). Use `dotnet build MobileGnollHackLogger.slnx` or `dotnet test MobileGnollHackLogger.slnx --filter-not-trait "Category=UsesExternalApi"` when building or testing the entire solution from the CLI.

## 1b. What to Expect from Live Gemini Calls

Before writing or debugging a test that calls the Google Gemini API, read
**[`docs/overseer/gemini-service-tier-measurements.md`](../../../docs/overseer/gemini-service-tier-measurements.md)**.
It records measured availability, latency, and `service_tier` behaviour per Gemini model, so
you can tell a genuine Overseer defect apart from a Google capacity condition.

The two facts that most often cause wasted debugging:

*   **The newest Gemini model may be effectively unavailable.** In the 2026-08-31 measurements,
    `gemini-3.7-flash` returned HTTP 503 or hung on **24 of 24** attempts, on both the `priority`
    and `standard` service tiers, while `gemini-3.6-flash` succeeded 24/24 in the same session.
    This is a provider capacity condition, not a bug in this codebase.
*   **Requesting `service_tier: priority` does not prevent 503s.** Google honours the request and
    fails anyway. Never write a test that asserts the absence of 503s.

Consequences for test design:

*   Apply the 429/503 tolerance in §2 below to **every** live Gemini test — it is mandatory here,
    not a nicety.
*   **Live tests must run against `gemini-3.5-flash-lite`.** The newer flash models are
    too slow for the suite: `gemini-3.6-flash` was measured at a ~2.4 s median with a
    59 s tail, and `gemini-3.7-flash` at zero successes in 24 attempts, versus ~0.7 s
    median for `gemini-3.5-flash-lite`. Never point a live test at the newest Gemini
    model, and **never choose a test model on availability alone — latency matters just
    as much.**
*   This is enforced by the allow-list in `Overseer.Tests/LiveApiModelPolicy.cs`, which
    defaults to `gemini-3.5-flash-lite` and is overridden with
    `AI:LiveTests:AllowedModels`. A disallowed model **skips** the test rather than
    failing it. When a new Gemini generation ships, treat the allow-list as something to
    re-measure, not to extend on faith.
*   Read the served tier from the response **body** (`usageMetadata.serviceTier`), never from the
    `x-gemini-service-tier` header, which Google omits on `:streamGenerateContent`.

> [!IMPORTANT]
> **The availability figures above expire.** The most recently released Gemini model is the most
> used and therefore usually the congested one. After a new Gemini generation ships, expect the
> congestion to move to the new model — re-measure rather than trusting the recorded numbers.

## 1c. What to Expect from Live Anthropic Calls

Before writing or debugging a test that calls the Anthropic API, read
**[`docs/overseer/anthropic-model-latency-measurements.md`](../../../docs/overseer/anthropic-model-latency-measurements.md)**.
It records measured latency, time-to-first-token, and availability for all seven supported Claude
models.

The picture is very different from Gemini, and the differences change how you write the test:

*   **Availability was not a problem.** 168 of 168 live calls returned HTTP 200 - no 429, no
    `529 overloaded_error`, no timeouts. Do not design Anthropic tests around scarcity the way the
    Gemini results require.
*   **Tolerate `529` anyway.** Anthropic's overload status is **`529 overloaded_error`**, not 503.
    Add it to the 429/503 tolerance rule in section 2 below - a single clean window is not a
    guarantee, and a test that fails on 529 will eventually be red through no fault of this codebase.
*   **Prefer `claude-sonnet-5` for live tests.** It was the most consistent model measured (~2.4 s
    median, worst call only 1.29x the median, nothing over 3.1 s) and the cheapest per call.
    `claude-opus-4-7` was marginally faster but costs 2.5x as much on output. **Avoid `claude-fable-5`**
    (~4.6 s median) **and `claude-sonnet-4-6`** (p90 of 7.7 s, which makes suite runtime unpredictable).
*   **Model ids carry no date suffix.** `claude-sonnet-5`, never `claude-sonnet-5-20260630` - a
    date-suffixed id returns `404 not_found_error`.
*   **Set `thinking` and `display` explicitly** in any test that compares models or asserts on timing.
    Omitting `thinking` runs adaptive thinking on Fable 5 / Opus 5 / Sonnet 5 but **no thinking at all**
    on Opus 4.8 / 4.7 / 4.6 / Sonnet 4.6, so a test that omits it is not testing what it looks like.

> [!NOTE]
> **`Overseer.Tests/LiveApiModelPolicy.cs` is Gemini-only** - its `DefaultModel` is
> `gemini-3.5-flash-lite` and it would reject any Claude id. Making it provider-aware is prerequisite
> work before a live Anthropic test can honour the recommendation above.

## 2. Graceful Error Handling (429 & 503)

External APIs are subject to rate limiting (`429 Too Many Requests`) and service unavailability (`503 Service Unavailable`). 

*   **Do not fail the test suite** if an external API returns a 429 or 503 error. These are expected network/quota conditions, not bugs in the Overseer codebase.
*   **Catch and Warn**: Wrap external API calls in a `try/catch` block. If a 429 or 503 exception is detected, log a warning to the `ITestOutputHelper` and let the test pass (e.g., `Assert.True(true); return;`).

## 3. Integration Testing Infrastructure

When testing ASP.NET Core controllers and endpoints:

*   Use `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>`.
*   **Disable Antiforgery**: Disable `AutoValidateAntiforgeryTokenAttribute` in the test factory to allow testing POST/PUT/DELETE methods without juggling CSRF tokens.
*   **Remove Hosted Services**: Remove background services like `SourceCodeService` that perform heavy local file I/O unless explicitly testing them.
*   **Mock Authentication**: Swap the real cookie authentication with a `TestAuthHandler` that can automatically log in a user based on an HTTP header (e.g., `X-Test-User`).
*   **Swap Database**: Replace the SQL Server DbContext with `UseInMemoryDatabase`.

## 4. Angular Frontend Testing (Overseer ClientApp)

When developing or modifying frontend code in `Overseer/ClientApp/`, unit tests must be executed to ensure client-side functionality and regression prevention.

### When to Run Angular Tests
- Whenever creating, modifying, or refactoring Angular components, services, pipes, or utility functions in `Overseer/ClientApp/`.
- After writing new unit tests (`*.spec.ts`) or updating existing test suites.
- As part of the verification step before completing any frontend task in the Overseer project.

### How to Run Angular Tests

Run commands from the `Overseer/ClientApp/` directory:

*   **Run Entire Test Suite Once (Headless - Preferred)**:
    ```bash
    npm run test:headless
    ```
    This is the command for this repository's implementation plans and verification runs, and it is also stated in **`AGENTS.md`**, which is loaded into every context window.
    *(Alternatively: `npx ng test --no-watch --browsers=ChromeHeadless` or `npm test -- --no-watch --browsers=ChromeHeadless`)*

*   **Run Specific Test File (Headless)**:
    ```bash
    npx ng test --include="src/app/chat/chat.component.spec.ts" --no-watch --browsers=ChromeHeadless
    ```

*   **Production Build Type/Template Check**:
    ```bash
    npm run build
    ```

> [!WARNING]
> **Single-Run & Headless Execution Required**: Always run tests with `--no-watch --browsers=ChromeHeadless` (or `npm run test:headless`).
> - **Headless Chrome**: Prevents disruptive browser GUI windows from opening on the user's desktop.
> - **No Watch**: Omitting `--no-watch` leaves Karma in continuous watch mode, causing background task execution to hang indefinitely.

### Angular Test Configuration Best Practices
*   **Router Dependencies**: Standalone components using `RouterModule`, `<a routerLink>`, or `ActivatedRoute` must include `provideRouter([])` in `TestBed.configureTestingModule({ providers: [provideRouter([])] })`.
*   **HTTP Dependencies**: Services or components utilizing `HttpClient` must include `provideHttpClient()` and `provideHttpClientTesting()` from `@angular/common/http/testing`.
*   **Static and Pure Logic**: For static methods (like `ChatComponent.stripThoughts`) or pure helper functions, test them directly without `TestBed` boilerplate to keep tests fast and isolated.

## 5. Background Indexed Services Synchronization

When testing services or tools that index files in the background (`WikiService`, `NetHackWikiService`, `KnowledgeBaseService`):

*   **Asynchronous Initialization**: Tests must be `async Task` methods.
*   **Await `InitializationTask`**: Always call `await service.InitializationTask;` before querying the service or executing tools to ensure background Lucene/file indexing has finished.
*   **Testing Cold Guards**: To test that a tool returns `Success = false` with a directive error message (`ToolGuardMessages`), execute the tool immediately **without** awaiting `service.InitializationTask`.
*   Refer to the `background_indexing_architecture` skill for full architectural details.

## 6. Accessing Internal Members in Unit Tests (`InternalsVisibleTo`)

When unit testing internal components, helper methods, or prompt builders in the `Overseer` project (such as `ChatService.BuildSystemPrompt`):

*   **Modern MSBuild Item Syntax**: Declare test assembly visibility directly in `Overseer.csproj` using `<InternalsVisibleTo>` inside an `<ItemGroup>`, rather than legacy `AssemblyInfo.cs` attributes:
    ```xml
    <ItemGroup>
      <InternalsVisibleTo Include="Overseer.Tests" />
    </ItemGroup>
    ```
*   **Encapsulation Principle**: Keep methods and helpers `internal` (rather than making them unnecessarily `public`) when they only need to be exposed to `Overseer.Tests` for unit testing while remaining hidden from external consumers.
*   **Isolated Unit Testing**: Prefer marking core prompt formatting, sanitizers, or parser methods as `internal` or `public static` so they can be tested directly and deterministically in isolation without requiring full end-to-end HTTP, SignalR, or AI provider streams.



