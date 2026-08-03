---
name: overseer_frontend
description: Guidelines for implementing and designing the frontend for the Overseer project.
---
# Overseer Frontend Guidelines

When working on the frontend for the Overseer project within MobileGnollHackLogger, follow these guidelines:

1. **Prefer Angular Components**: Whenever implementing frontend features or UI elements, always prefer using Angular components.
2. **Follow Google's Modern Web Guidance**: Ensure that the frontend design and implementation adhere strictly to Google's Modern Web Guidance. This includes utilizing modern web development best practices for UI/Layout, Scroll/Motion, Performance, and System/APIs.

**IMPORTANT**: When working on HTML/CSS and client-side JS tasks, you MUST execute the `modern-web-guidance` skill FIRST to ensure up-to-date best practices are applied.

## Styling UI Elements

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
