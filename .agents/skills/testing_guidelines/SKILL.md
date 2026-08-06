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
