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
*   **CLI Instructions in Code**: The test file must contain a human-readable header comment instructing developers and agents on how to skip these tests during normal execution.
    ```csharp
    // To run tests while SKIPPING this file (to save AI API quota), use:
    // dotnet test --filter "Category!=UsesExternalApi"
    ```

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


