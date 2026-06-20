---
name: MobileGnollHackLogger Pages and Frontend
description: Reference for Razor Pages, Identity logic, layout, styling, and test pages. Covers data bindings for TopScores, RecentGames, and Statistics.
---

# Razor Pages & Frontend (`Pages/`)

## 🖥️ Shared Layout & Styling
*   **Layout:** `Pages/Shared/_Layout.cshtml` uses Bootstrap 5 Dark Theme (`data-bs-theme="dark"`).
*   **Styling Source:** `wwwroot/css/site2.scss` compiled to `site2.css`.
*   **Interactivity:** Relies on jQuery and DataTables (`jquery.dataTables.min.js`). DataTable rows are clickable and navigate to dump logs.
*   **Login Partial:** `_LoginPartial.cshtml` handles auth dropdowns.

## 📄 Core Public Pages
*   **`Index` (`/`)**: Landing page. Instructions change based on auth state.
*   **`TopScores`**: Leaderboard. Driven by `DeathModel`. Filters: `Mode` (normal/modern), `Death` ("ascended"). Queries `GameLog` where `Scoring == "yes"`. Ordered by `Points DESC`.
*   **`RecentGames`**: Same filters as TopScores, but ordered by `EndTimeUTC DESC`.
*   **`Statistics`**: Win rate matrix (Roles × Difficulties). Driven by `ModeModel`. Excludes `debug`/`explore` modes and requires `Turns >= 1000`.
*   **`BonesTransactions`**: Leaderboard for bones sharing. Tabbed by difficulty.

## 🔐 Identity Pages (`Areas/Identity/Pages/`)
*   **Login:** Auth is by **UserName** (not email). Uses `PasswordSignInAsync`.
*   **Register:** Requires confirmed email. Username regex: `^[A-Za-z0-9][A-Za-z0-9_]{0,30}$`.
*   **Manage/JunetHack:** Allows users to set their `JunetHackUserName` for the external tournament integration.

## 📧 Email Templates
*   Location: `Content/ConfirmAccountEmail.html`, `Content/ForgotPasswordEmail.html`.
*   Placeholders like `{CallbackUrl}` and `{UserName}` are injected by Identity scaffolding.

## 🧪 Test Pages (DO NOT REMOVE)
Browser-based forms for manually testing APIs.
*   `Test.cshtml` → `POST /xlogfile`
*   `TestBones.cshtml` → `POST /bones`
*   `TestSFTCreate.cshtml` → `POST /api/SaveFileTracking/create`
*   `TestSFTUse.cshtml` → `POST /api/SaveFileTracking/use`
