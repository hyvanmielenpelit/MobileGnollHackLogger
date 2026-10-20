/**
 * True when a keydown belongs to an IME composition, so Enter or Escape is the IME's to
 * commit or cancel the conversion rather than the field's to submit or close. keyCode 229
 * catches Safari, which fires compositionend before the confirming keydown (WebKit bug 165004).
 * Only meaningful on keydown: the keyup of the confirming key arrives after compositionend.
 */
export function isImeComposing(event: Event): boolean {
  const keyboardEvent = event as KeyboardEvent;
  return keyboardEvent.isComposing || keyboardEvent.keyCode === 229;
}
