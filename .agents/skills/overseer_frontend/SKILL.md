---
name: overseer_frontend
description: Guidelines for implementing and designing the frontend for the Overseer project.
---
# Overseer Frontend Guidelines

When working on the frontend for the Overseer project within MobileGnollHackLogger, follow these guidelines:

1. **Prefer Angular Components**: Whenever implementing frontend features or UI elements, always prefer using Angular components.
2. **Component Structure**: Always use separate files for templates (`.html`) and styles (`.scss`). Do not use inline templates or styles in the component TypeScript file.
3. **No Basic JS Popups**: NEVER use `alert()`, `confirm()`, or `prompt()` JavaScript popups under any circumstances. When these are needed, use inline error messages or a native `<dialog>` for confirmations and inputs, per **Popups and Modals** below.
4. **Follow modern web platform best practices**: UI/Layout, Scroll/Motion, Performance, Accessibility, and System/APIs. The baseline is in **Modern Web Baseline** below, and it binds in every harness.

### Before writing HTML, CSS, or client-side JS

**If your harness provides a `modern-web-guidance` skill, execute it first.** It ships with
Antigravity (it is Google's) and carries more current and more detailed guidance than this file.

**If it does not — Claude Code does not, and neither does `hyvanmielenpelit/SharedAgentSkills` —
then the Modern Web Baseline below is the standard, and it is sufficient to proceed.** Do not stall
waiting for a skill your harness has no way to load, and do not silently skip the requirement
either: say in chat which of the two applied.

> [!NOTE]
> Claude Code's nearest skills are `artifact-design` (design fundamentals for self-contained Claude
> Artifact pages) and `dataviz` (chart and dashboard design). **Neither is a substitute here** —
> they target standalone generated pages, not an existing Angular application with its own design
> system. Use them only if the task really is a chart or a standalone artifact.

## Modern Web Baseline

Harness-neutral, and the floor for any Overseer frontend work.

### Semantics and accessibility
- Interactive controls are real elements: `<button type="button">`, `<a href>`, `<label>` — never a `div` with a click handler.
- Every control has an accessible name. An icon-only button needs `aria-label`, and the name must be **distinct**: `aria-label="Copy reply to question 3"`, not three buttons all named "Copy".
- State the user must notice goes in a live region: `aria-live="polite"` for transient confirmations ("Copied"), `role="status"` for progress that advances on its own. A change that is only visible is invisible to a screen-reader user.
- Keyboard reachable in a sensible order, with a **visible** focus ring. Never `outline: none` without a replacement.
- Respect `prefers-reduced-motion` for spinners, transitions, and auto-scrolling.
- Colour is never the only carrier of meaning — pair it with text or shape.

### Layout and content
- Long or unbounded content gets an explicit strategy: `overflow-x: auto` on wide blocks, `white-space: pre-wrap` plus a collapsed max-height and an expand control on long text. A page that grows without limit is a defect, not a detail.
- When content is collapsed or truncated for display, actions on it (copy, download, export) still operate on the **full** value.
- Prefer modern layout primitives (flex, grid, logical properties, container queries) over fixed pixel scaffolding.

### Platform APIs
- Feature-detect before use, and handle rejection. `navigator.clipboard` is **undefined outside a secure context** and can reject even inside one — show an inline failure message, never a bare `console.error`.
- Object URLs from `URL.createObjectURL` are revoked after use.
- Filenames built from user or model data are sanitised to a whitelist (`[A-Za-z0-9._-]`), never interpolated raw.
- Never render untrusted or model-generated text as HTML. Plain text in `<pre>`, never `[innerHTML]`.

### Angular specifics
- Standalone components; `@if` / `@for` control flow with `track`; `OnPush` change detection with explicit `markForCheck()`.
- Every subscription, timer, and observer is torn down in `ngOnDestroy`.
- Polling uses `switchMap` so requests cannot stack, and backs off on error rather than hammering.

## Styling UI Elements

### Global Styles
- **`Overseer/ClientApp/src/styles.scss` is the EXCLUSIVE global styles file** for the Overseer Angular project.
- **Do NOT modify `site2.scss` or `site2.css`** when working on the Overseer project (those files are strictly for the main ASP.NET MobileGnollHackLogger web pages).
- If you find duplicate styles across multiple component SCSS files (e.g. `.settings-container` or `.header-row`), move them to `styles.scss` for centralized management.

When creating UI elements in the Overseer frontend, adhere to the following standards:

### Buttons
- Always use the `.btn-gh` class (and variants like `.btn-gh-cancel` and `.btn-gh-delete`) for primary actions to match the GnollHack theme.
- For small utility buttons (like info icons), create specific CSS classes (e.g., `.btn-info`) in the component's SCSS file, utilizing modern styling (rounded corners, subtle hover glows).
- Common button styles are managed centrally in `styles.scss`. Avoid duplicating base button styles.

### Popups and Modals
- **Use the Native `<dialog>` Element**: New popups and modals must use the semantic HTML `<dialog>` element rather than custom `div`-based overlays.
- Apply the `.gh-dialog` class to `<dialog>` elements for consistent theming (glassmorphism, padding, backdrop).
- Control the dialog via its native API (`dialog.showModal()` and `dialog.close()`) using Angular `@ViewChild` references.
- DO NOT use `.modal-overlay` wrappers for new popups, as `<dialog>` provides native accessibility, focus management, and top-layer positioning.

### Tooltips
- **Always use the Interest-Triggered Tooltip Component**: Instead of using the browser's native `title` attribute for tooltips (which is visually inconsistent and not easily accessible), always implement custom popovers.
- **Implementation**: Add the `interestfor="tooltip-id"` attribute to the trigger (e.g. the button). Provide a corresponding `<div popover="hint" id="tooltip-id" class="gh-tooltip">...</div>`.
- **Styling**: Center the tooltip appropriately utilizing `anchor()` functions and standard best practices.
- **CRITICAL**: Never use the `title` attribute in conjunction with a custom tooltip, as they will conflict and overlap.

### Error Handling & User Prompts
- **Avoid Basic JS Dialogs**: As stated in the main rules, do NOT use basic JavaScript `alert()`, `prompt()`, or `confirm()` dialogs. They disrupt user flow, unfocus elements, and look outdated.
- **Use Modern Equivalents**: Implement inline error messaging (e.g., displaying error text near an input field) or use styled `<dialog>` modals for confirmation/input prompts, ensuring integration with Angular state and modern web guidance.

### Icons
- **Use SVGs exclusively**: DO NOT use Unicode emojis (e.g., ✨, 🐛, 🚀) for UI elements or icons. They are notoriously difficult to align correctly, render inconsistently across operating systems, and look unprofessional.
- Always use precise `<svg>` icons configured with `fill="currentColor"` so they seamlessly match the surrounding text color and align perfectly using Flexbox (`display: flex; align-items: center`).

### Checkboxes
- **Use the `.checkbox-label` Pattern**: Checkboxes should be wrapped inside a `<label class="checkbox-label">` element containing the `<input type="checkbox">` and the descriptive label text.
- **Dimensions & Theme**: Checkbox inputs are sized at 20x20px with a 10px flex gap between the input and text, and styled with the golden theme accent color (`accent-color: var(--primary-color, #d4af37)`).
- **Multi-Line Alignment (`.align-start`)**: When the checkbox copy spans multiple lines or contains descriptive subtext, add the `.align-start` class modifier to top-align the checkbox with the first line of text.
- **Centralized Styling**: Checkbox styling is managed centrally in `src/styles.scss`. Do not duplicate checkbox CSS rules in component SCSS files.

## Project Structure and Navigation

The Overseer project is an ASP.NET Core backend serving an Angular frontend.
- **Frontend (Angular)**: Located in `Overseer/ClientApp/src/app/`. Contains all components, services, and routes.
- **Backend (ASP.NET Core)**: 
  - `Overseer/Controllers/`: API Endpoints
  - `Overseer/Hubs/`: SignalR Hubs (e.g., for real-time chat)
  - `Overseer/Services/`: Backend business logic
  - `Overseer/Models/`: Data Models

### Static Assets and Build Output (`wwwroot` vs `public`)
- **NEVER put source files, source images, or static assets directly into `Overseer/wwwroot/`.**
- The entire `Overseer/wwwroot/` folder is a **build output directory**. It is wiped and repopulated entirely by the Angular `npm run build` process. 
- Any files manually placed in `Overseer/wwwroot/` will be permanently deleted upon the next build.
- **Where to put assets**: Place all new images, icons, and static files in the Angular source directory: `Overseer/ClientApp/public/` (or `Overseer/ClientApp/public/img/`).
- During the build, Angular will automatically copy everything from `ClientApp/public/` into `wwwroot/`.
- **Git Tracking**: Because `wwwroot/` is strictly a build output, none of its contents should be tracked by Git. The `.gitignore` at the repository root prevents it from being committed.

### Pages (Routes)
The Angular application's routes are defined in `app.routes.ts`. The primary pages include:
- `/chat` (`chat.component`): The main chat interface.
- `/settings` (`settings.component`): User preferences.
- `/api-keys` (`api-keys.component`): Management of user API keys.
- `/models` (`models.component`): AI Model selection and configuration.
- `/admin` (`admin.component`): System administration (groups, configs, rate limits).
- `/debug-log` (`debug-log.component`): Developer debug logs.
- `/login` (`login.component`): Authentication entry point.

### Popups (`<dialog>` elements)
To find specific popups, look in the corresponding component's `.html` template:

- **Admin Component (`admin.component.html`)**
  - `#manageGroupsDialog`: Manage Groups
  - `#createGroupDialog`: Create Group
  - `#configDialog`: Config
  - `#confirmDialog`: Confirm
  - `#manageUserConfigsDialog`: Manage User Configs
  - `#manageGroupConfigsDialog`: Manage Group Configs
  - `#editConfigOverrideDialog`: Edit Config Override
  - `#rateLimitsDialog`: Rate Limits
  - `#analyticsDialog`: Analytics

- **API Keys Component (`api-keys.component.html`)**
  - `#apiKeyInfoDialog`: API Key Info

- **Chat Component (`chat.component.html`)**
  - `#deleteConfirmDialog`: Delete Confirm
  - `#imagePreviewDialog`: Image Preview
  - `#reportConfirmDialog`: Report Confirm
  - `#logoutDialog`: Logout

- **Admin Alerts Component (`admin-alerts.component.html`)**
  - `#popoverContainer`: System alert popover banner displaying missing configuration warnings from `AdminAlertService` (`/api/admin/system-alerts`) to admin users.

- **Models Component (`models.component.html`)**
  - `#modelPickerDialog`: Model Picker
  - `#editModelDialog`: Edit Model
  - `#deleteModelConfirmDialog`: Delete Model Confirm

- **Settings Component (`settings.component.html`)**
  - `#confirmDialog`: Confirm
  - `#changelogDialog`: Changelog

## Component Reuse and State Management

The Overseer frontend utilizes a custom `RouteReuseStrategy` (indicated by `data: { reuse: true }` in `app.routes.ts`) for primary views like the `ChatComponent`. This prevents the component from being destroyed when navigating away, ensuring chat history and UI state are preserved.

However, this design introduces a critical pitfall: **`ngOnInit()` is NOT triggered when navigating back to a reused component.**

### The Correct Design Pattern
If a user changes settings, API keys, or models on a different page and navigates back to the chat window, the chat window must reflect these changes immediately. To achieve this, use the following patterns:

1. **Router Navigation Events (Recommended for data fetching)**
   Subscribe to Angular's router `NavigationEnd` events in the reused component. When detecting a re-entry to the component's route, explicitly call a data-loading method (e.g., `loadSettings()`) to fetch fresh state.

   ```typescript
   // In the reused component (e.g., ChatComponent)
   let previousUrl = '';
   this.router.events.pipe(
     filter(event => event instanceof NavigationEnd)
   ).subscribe((event: any) => {
     const currentUrl = event.urlAfterRedirects;
     if (currentUrl && currentUrl.startsWith('/chat')) {
       if (previousUrl && !previousUrl.startsWith('/chat')) {
         // We re-entered the component. Refetch data.
         this.loadSettings(false); 
       }
     }
     previousUrl = currentUrl || '';
   });
   ```
   **CRITICAL**: Extract your initialization logic from `ngOnInit()` into a dedicated `loadSettings(isInit: boolean)` method so it can be called safely on both initial load and re-entry.

2. **Shared RxJS State (Recommended for single-value live UI updates)**
   Use `BehaviorSubject` or `Subject` in shared services (like `SettingsService` or `AuthService`). The reused component should subscribe to these observables in `ngOnInit()` so that any updates pushed by other pages automatically trigger UI updates in the background.

   ```typescript
   // In SettingsService
   public showThoughtsAndToolsUpdated = new Subject<number>();
   
   // In SettingsComponent (firing the change)
   this.settingsService.showThoughtsAndToolsUpdated.next(newValue);
   
   // In ChatComponent (listening)
   this.settingsService.showThoughtsAndToolsUpdated.subscribe(val => {
     this.showThoughtsAndTools = val;
   });
   ```

3. **Prevent HTTP Caching**
   When explicitly re-fetching data via HTTP GET upon component re-entry, ensure the request includes `no-cache` headers. Otherwise, the browser may return a stale cached response from before the settings were changed.

   ```typescript
   this.http.get<MyData>('/api/data', {
     headers: {
       'Cache-Control': 'no-cache',
       'Pragma': 'no-cache',
       'Expires': '0'
     }
   });
   ```

## Avatar Animation State Machine

The Overseer chat interface (`chat.component.ts`) features an animated avatar that reacts to the AI's generation process. When modifying the avatar logic, adhere strictly to these rules to ensure the animations correctly represent the AI's internal state.

Never use simplistic flags (like applying `thinking` for the entire streaming duration) that violate this matrix.

### Animation Lifecycle

The avatar follows a strict lifecycle from the moment the user clicks Send to the end of the response:

1. **User clicks Send** → **Thinking** animation plays immediately (waiting for the server to respond).
2. If the server takes longer than 30 seconds to respond → **Yawning** replaces Thinking. (Thinking and Yawning only happen in this initial wait phase).
3. When the first `thinking_chunk` or `tool_start` event arrives → **Tool Use** animation plays.
4. **Tool Use** animation plays continuously during all active work. It NEVER reverts to Thinking or Yawning, even if the server is silent for a long time.
5. When the main content (final response) starts streaming → **Talking** animation plays.
6. When the response finishes → **Static Image** (idle). This is a **terminal state** that never auto-transitions to any animation.

### Animation Rules

| Animation | Allowed to Play When | Angular Condition | Priority |
| :--- | :--- | :--- | :---: |
| **Static Image (idle)** | When no streaming is active. This is both the initial state and the **terminal state** after a response finishes. A static image NEVER auto-transitions to any animation; only a new user Send action restarts the animation cycle. | `this.isStreaming === false` | 1 (highest) |
| **Talking** | ONLY when the main content (final response) is actively streaming to the user. | `this.hasRealContent === true` | 2 |
| **Tool Use** | During the entire pre-response **active working phase**. This includes actively streaming native "thinking texts" (reasoning chunks), buffering pre-tool preamble text, or actively executing a tool call. Once the avatar enters Tool Use, it remains in Tool Use until Talking. | `this.hasEnteredWorkingPhase === true` | 3 |
| **Thinking** | ONLY during the **initial wait phase** before the server has started returning any active work (tools/thinking texts). Once the working phase starts, Thinking can NEVER play again for that message. | `this.isStreaming === true`<br>AND none of the above are true | 4 |
| **Yawning** | ONLY when the **Thinking** animation has been playing continuously for more than 30 seconds. It must NEVER trigger from any other animation state (Talking, Tool Use, or Static Image), regardless of how long those states last. | `desired === 'thinking'`<br>AND cumulative Thinking time > 30,000 ms | 5 (lowest) |

### Key Constraints

- **Yawning is exclusive to Thinking:** The yawning timer only counts time spent in the Thinking state. Because the server might be completely silent (e.g., repeatedly returning 503 errors and retrying) during the initial wait phase, `updateDesiredAvatarState()` schedules a dedicated `yawningCheckTimeout` exactly 30 seconds into the `thinking` state to guarantee the transition to yawning happens even without incoming server events. This timer is cleared as soon as the avatar enters `toolUse` or `idle`.
- **Session State Persistence:** When the user switches between different chat sessions, `ChatComponent` persists its internal timing state (`thinkingAnimStartTime` and `hasEnteredWorkingPhase`) in a `sessionStateMap` keyed by `sessionId`. This ensures that if the avatar was yawning before switching chats, returning to the chat restores the original elapsed time and instantly resumes the yawning animation, rather than restarting the 30-second timer from zero.
- **All animations wait for loop end by default:** No animation is interrupted mid-loop. This is controlled by the `INTERRUPTIBLE_ANIMATIONS` static set in `ChatComponent`. By default this set is empty, meaning all animations play their current loop to completion before switching. To make an animation immediately interruptible, add its name to the set.
- **`lastNetworkActivityTime`** was used historically but is no longer relied upon for state transitions, as the avatar is now strictly phase-locked into Tool Use once work begins.

### Smooth Animation Transitions

Each avatar animation is a looping animated WebP with a known loop duration (defined in `AVATAR_LOOP_DURATIONS`). To avoid jarring visual snaps (e.g., a hammer disappearing mid-swing), transitions between animations must respect loop boundaries. The `requestAvatarTransition()` method implements this system:

**Architecture:**
1. `updateDesiredAvatarState()` determines *what* the avatar should be doing based on the current flags.
2. `requestAvatarTransition(newState)` determines *when* to switch, using loop-boundary scheduling.
3. `applyAvatarState(state)` performs the actual DOM swap (changing `currentAvatarState`, which updates the `<img>` src).

**Transition rules per animation (default configuration):**

| Current Animation | Can be Interrupted Mid-Loop? | Configurable? |
| :--- | :--- | :--- |
| **Static Image (idle)** | Yes, always | No — idle is not an animation, so it is always immediately replaceable. |
| **Thinking** | No — waits for loop end | Yes — add `'thinking'` to `INTERRUPTIBLE_ANIMATIONS` to allow mid-loop interrupts. |
| **Tool Use** | No — waits for loop end | Yes — add `'toolUse'` to `INTERRUPTIBLE_ANIMATIONS` to allow mid-loop interrupts. |
| **Talking** | No — waits for loop end | Yes — add `'talking'` to `INTERRUPTIBLE_ANIMATIONS` to allow mid-loop interrupts. |
| **Yawning** | No — waits for loop end | Yes — add `'yawning'` to `INTERRUPTIBLE_ANIMATIONS` to allow mid-loop interrupts. |

The `INTERRUPTIBLE_ANIMATIONS` set is a `static readonly` constant in `ChatComponent`, located near the other avatar constants (`AVATAR_LOOP_DURATIONS`, `AVATAR_SRCS`). To change interruptibility, simply add or remove animation names from this set.

**Loop-boundary scheduling mechanism:**
- When a non-interruptible animation is playing, `requestAvatarTransition()` calculates the time remaining until the current loop ends: `timeUntilLoopEnd = loopDuration - (elapsed % loopDuration)`.
- It schedules a `setTimeout` for `timeUntilLoopEnd` milliseconds. When the timer fires, the latest `pendingAvatarState` is applied.
- If multiple state change requests arrive while a timer is already pending, only the `pendingAvatarState` value is updated — the timer is not rescheduled. This ensures the swap happens at the original loop boundary with the most up-to-date desired state.
- The `done` event handler has its own special loop-boundary wait (`executeDone` callback) that defers the final transition to idle and the commit of the message to `this.messages` until the current animation loop finishes gracefully.

## Chat Message Handling & Clipboard Specifications

Refer to the dedicated [`overseer_chat_message_handling`](file:///c:/hmp/MobileGnollHackLogger/.agents/skills/overseer_chat_message_handling/SKILL.md) skill for the full specification. Key rules include:

- **Thinking Blocks (`<div class="ai-thought">`)**: Thinking tokens and pre-tool reasoning are wrapped in `<div class="ai-thought">...</div>`.
- **Clipboard Copying (`.copy-btn`)**: When copying messages to the clipboard, all `<div class="ai-thought">` blocks and enclosed reasoning content MUST be stripped using `ChatComponent.stripThoughts()`, regardless of user visibility settings. Code block formatting inside the response is preserved.
- **Tool Result Copying (`.tool-copy-btn`)**: Individual tool call outputs are copied separately via their dedicated copy button, copying only `tc.result`.

## Conversation Loading & Exclusivity Lifecycle

When switching between chat sessions or loading a conversation in `ChatComponent`, adhere strictly to these rules:

1. **Mutual Exclusivity**: The "Loading conversation..." spinner and text (`isLoadingSession`) MUST NEVER be visible simultaneously with chat messages (`messages`), streaming bubbles, tool calls, handoff overlays (`isHandoffWaiting`), or settings warnings.
2. **Never in the Middle of an Active Chat**: The conversation loading state (`isLoadingSession`) is an exclusive full-panel state used ONLY during explicit user navigation between distinct sessions or initial page load. It must NEVER be triggered in the middle of an active conversation, during AI generation/streaming, or while waiting for a response.
3. **No Aggressive Watchdogs During Streaming**: Do NOT use aggressive client-side polling or timeout watchdogs during active streaming that call destructive session reloads (`loadSession()`). Complex AI reasoning, thinking tokens, and tool executions regularly exceed 10–30 seconds.
4. **Non-Destructive Background Sync**: When reconnecting to SignalR or synchronizing session state in the background for the current chat session, always use silent in-place synchronization (`syncSessionSilently`). Never set `isLoadingSession = true`, never clear `messages`, never reset avatar state, and never display the loading spinner during background re-syncs.
5. **Immediate State Reset on Navigation**: Only upon initiating a genuine session switch (`loadSession(id)` where `id !== currentSessionId`), the component purges previous session messages (`this.messages = [];`) and active streaming state (`this.clearStreamingState()`), and sets `this.isLoadingSession = true;`.
6. **Template Enforcement**: In `chat.component.html`, the message area must use structural conditional branches (`@if (isLoadingSession) { ... } @else { ... }`) to guarantee that message containers and loaders cannot coexist in the DOM.
7. **Loader Styling**: The conversation loader must use a semantic SCSS class (`.conversation-loader`) centered within the `.messages` container, with no ad-hoc inline styles.
8. **Scroll Position on Load Completion**: When a session finishes loading, the chat view must automatically scroll to the bottom of the conversation (`scrollToBottomClamped(false)`), ensuring that the user is positioned at the newest messages and not stuck at scroll position 0 (top).

## AI Model Form Property Preservation & Provider Lifecycle

When configuring or editing AI models in `AiModelFormComponent` (used across `/models` and `/admin` config dialogs), the form distinguishes between **Add Mode** and **Edit Mode**:

### 1. Add Mode vs. Edit Mode Behavior
- **Add Mode (`mode === 'add'`)**: Selecting a model from the dropdown automatically populates all property fields with the model's defaults (e.g. recommended or medium thinking level, default reasoning mode/summary, and maximum input/output token limits).
- **Edit Mode (`mode === 'edit'`)**: When modifying an existing model within the same provider, all existing property values configured by the user are preserved unless they are invalid or unsupported by the newly selected model.

### 2. Property Retention Rules (Same Provider in Edit Mode)
- **Effective Value Resolution**: The component resolves effective property values by evaluating both standard dropdown selections and custom text input fields.
- **Thinking Level, Reasoning Mode, Reasoning Summary, Service Tier**:
  - If the existing configured value is supported by the new model (or is empty / Default), it remains unchanged.
  - If the existing value is unsupported by the new model, it gracefully falls back to the new model's recommended default (e.g., `recommendedThinkingLevel` or `'medium'`); if the feature is entirely unsupported by the new model, the property resets to `''`.
- **Token Limits (`maxInputTokens` / `maxOutputTokens`)**:
  - User-configured limits are preserved as long as they do not exceed the new model's maximum capacity.
  - If a configured token limit exceeds the new model's maximum limit, it is clamped to that maximum limit.
  - If the limit was previously unconfigured (`null`), it remains unconfigured.
- **Switching to "Custom..." Model**:
  - If the user switches the model dropdown to "Custom...", all current property values are preserved by setting the picker dropdowns to `'custom'` and placing the retained values into the corresponding custom text inputs.

### 3. Provider Immutability in Edit Mode
- **Provider Immutability**: In Edit Mode (`mode === 'edit'`), the Provider dropdown is disabled (`[disabled]="mode === 'edit'"`) and programmatic provider changes via `onProviderChange()` are ignored. The AI Provider can only be chosen during creation in Add Mode (`mode === 'add'`).
- Because the provider cannot change during edits, property preservation always operates within models of the same provider.

## Angular Unit Testing

Always execute unit tests before completing frontend modifications in Overseer. Refer to [`testing_guidelines`](file:///c:/hmp/MobileGnollHackLogger/.agents/skills/testing_guidelines/SKILL.md) for full instructions.

- **Run Single-Run Headless Suite (Preferred)**:
  ```bash
  npm run test:headless
  ```
  *(Or: `npx ng test --no-watch --browsers=ChromeHeadless`)*
- **Single Test File (Headless)**:
  ```bash
  npx ng test --include="src/app/chat/chat.component.spec.ts" --no-watch --browsers=ChromeHeadless
  ```
- **Build Verification**:
  ```bash
  npm run build
  ```



