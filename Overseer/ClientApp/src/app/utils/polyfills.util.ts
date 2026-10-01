let loaded = false;
let anchorPolyfill: (() => Promise<unknown>) | null = null;

/**
 * Conditionally loads the popover, interest-invoker, and anchor-positioning
 * polyfills needed by the `interestfor` + `popover="hint"` tooltip pattern.
 *
 * Every import is feature-detected and dynamic, so a browser with native
 * support downloads nothing. Safe to call from any number of components; the
 * work happens once per page load.
 */
export function ensureOverlayPolyfills(): void {
  if (loaded) {
    return;
  }
  loaded = true;

  if (!('popover' in HTMLElement.prototype)) {
    import('@oddbird/popover-polyfill')
      .catch(err => console.warn('Failed to load popover polyfill', err));
  }
  if (!('interestForElement' in HTMLButtonElement.prototype)) {
    // @ts-ignore - package ships no type declarations
    import('interestfor')
      .catch(err => console.warn('Failed to load interestfor polyfill', err));
  }
  if (!('anchorName' in document.documentElement.style)) {
    import('@oddbird/css-anchor-positioning/fn')
      .then(mod => {
        anchorPolyfill = mod.default;
        return anchorPolyfill();
      })
      .catch(err => console.warn('Failed to load anchor positioning polyfill', err));
  }
}

/**
 * Re-runs the anchor-positioning polyfill after anchors or targets have been added
 * to the DOM. The polyfill does not observe DOM mutations, so a control rendered
 * behind an @if — an admin tab panel, for instance — is invisible to its first scan.
 *
 * A no-op where the browser supports anchor positioning natively, and safe to call
 * before the polyfill has finished loading.
 */
export function refreshAnchorPositioning(): void {
  anchorPolyfill?.().catch(err => console.warn('Anchor positioning refresh failed', err));
}

/** Set on <html> when anchor positioning and position-area are native; never alongside the polyfill. */
export const NATIVE_ANCHOR_CLASS = 'gh-anchor-native';

/**
 * Marks the document for the native-only placement rules (`.gh-anchor-native` in styles.scss and
 * config-filter.component.scss). The anchor-positioning polyfill ignores `@supports`, so those rules
 * are gated on this class instead, which it never matches.
 */
export function markNativeAnchorPositioning(root: HTMLElement = document.documentElement): void {
  if ('anchorName' in root.style && CSS.supports('position-area', 'block-end')) {
    root.classList.add(NATIVE_ANCHOR_CLASS);
  }
}
