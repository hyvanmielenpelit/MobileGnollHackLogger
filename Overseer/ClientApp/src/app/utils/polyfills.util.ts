let loaded = false;

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
    // @ts-ignore - package ships no type declarations
    import('@oddbird/css-anchor-positioning')
      .catch(err => console.warn('Failed to load anchor positioning polyfill', err));
  }
}
