/** The tab index a tab-row key moves to (frontend_ui_controls § 5), or null when the key is not a tab-row key. */
export function rovingTabTarget(key: string, index: number, count: number): number | null {
  if (count <= 0) return null;
  switch (key) {
    case 'ArrowRight': return (index + 1) % count;
    case 'ArrowLeft': return (index - 1 + count) % count;
    case 'Home': return 0;
    case 'End': return count - 1;
    default: return null;
  }
}

/** `rovingTabTarget` for a keydown (frontend_ui_controls § 5), calling `preventDefault` only for a handled key. */
export function rovingTabIndex(event: KeyboardEvent, index: number, count: number): number | null {
  const next = rovingTabTarget(event.key, index, count);
  if (next !== null) event.preventDefault();
  return next;
}
