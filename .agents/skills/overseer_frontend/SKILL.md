---
name: overseer_frontend
description: Guidelines for implementing and designing the frontend for the Overseer project.
---
# Overseer Frontend Guidelines

When working on the frontend for the Overseer project within MobileGnollHackLogger, follow these guidelines:

1. **Prefer Angular Components**: Whenever implementing frontend features or UI elements, always prefer using Angular components.
2. **Component Structure**: Always use separate files for templates (`.html`) and styles (`.scss`). Do not use inline templates or styles in the component TypeScript file.
3. **Follow Google's Modern Web Guidance**: Ensure that the frontend design and implementation adhere strictly to Google's Modern Web Guidance. This includes utilizing modern web development best practices for UI/Layout, Scroll/Motion, Performance, and System/APIs.

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
- **Avoid Basic JS Dialogs**: Do NOT use basic JavaScript `alert()`, `prompt()`, or `confirm()` dialogs. They disrupt user flow, unfocus elements, and look outdated.
- **Use Modern Equivalents**: Implement inline error messaging (e.g., displaying error text near an input field) or use styled `<dialog>` modals for confirmation prompts, ensuring integration with Angular state and modern web guidance.

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
