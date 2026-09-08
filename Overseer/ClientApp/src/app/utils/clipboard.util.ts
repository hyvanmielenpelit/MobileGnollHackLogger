/**
 * Writing text to the system clipboard, with the two failures the async Clipboard API actually has.
 *
 * `navigator.clipboard` is **undefined outside a secure context** — a plain-HTTP origin, and some
 * embedded and test environments — so the property has to be probed rather than assumed. Even
 * inside a secure context `writeText` returns a promise that rejects: the permission can be denied,
 * the document can have lost focus, or the platform can refuse the write outright.
 *
 * So the contract is a boolean rather than a void promise. A caller has to be able to say "copy
 * failed, select the text instead", and it can only say that if it is told.
 */

/** True when the text reached the clipboard. False on an insecure context or a rejected write. */
export async function copyToClipboard(text: string): Promise<boolean> {
  if (typeof navigator === 'undefined' || !navigator.clipboard?.writeText) {
    return false;
  }
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    // A rejected write is an expected outcome, not a fault: the caller renders the fallback.
    return false;
  }
}
