---
name: overseer_frontend
description: Guidelines for implementing and designing the frontend for the Overseer project.
---
# Overseer Frontend Guidelines

When working on the frontend for the Overseer project within MobileGnollHackLogger, follow these guidelines:

1. **Prefer Angular Components**: Whenever implementing frontend features or UI elements, always prefer using Angular components.
2. **Component Structure**: Always use separate files for templates (`.html`) and styles (`.scss`). Do not use inline templates or styles in the component TypeScript file.
3. **No Basic JS Popups**: NEVER use `alert()`, `confirm()`, or `prompt()` JavaScript popups under any circumstances. When these are needed, handle them using Modern Web Guidance best practices (e.g., inline error messages, custom `<dialog>` components for confirmations/inputs).
4. **Follow Google's Modern Web Guidance**: Ensure that the frontend design and implementation adhere strictly to Google's Modern Web Guidance. This includes utilizing modern web development best practices for UI/Layout, Scroll/Motion, Performance, and System/APIs.

**IMPORTANT**: When working on HTML/CSS and client-side JS tasks, you MUST execute the `modern-web-guidance` skill FIRST to ensure up-to-date best practices are applied.

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
- **Use the Native `<dialog>` Element**: Following Modern Web Guidance, new popups and modals must use the semantic HTML `<dialog>` element rather than custom `div`-based overlays.
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



