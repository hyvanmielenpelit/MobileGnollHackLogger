/** Scrolls `listbox` just enough to show `option`, leaving every ancestor scroller alone. */
export function scrollOptionIntoListbox(listbox: HTMLElement, option: HTMLElement): void {
  const rect = option.getBoundingClientRect();
  const top = listbox.getBoundingClientRect().top + listbox.clientTop;
  const bottom = top + listbox.clientHeight;
  if (rect.top < top) {
    listbox.scrollTop -= top - rect.top;
  } else if (rect.bottom > bottom) {
    listbox.scrollTop += rect.bottom - bottom;
  }
}

/**
 * The least block size `listbox` must keep so that `popup`, which opens downward and is about to
 * change its rows, still reaches the visible end of its scroll container; null when it may fit its
 * rows. An absolutely positioned popup past that end extends the container's scrollable area, and
 * shrinking it there would clamp the container's scrollTop. `removedBefore` is the block size about
 * to be removed above the listbox inside the popup, such as a toolbar being collapsed.
 */
export function scrollSafeListHold(popup: HTMLElement, listbox: HTMLElement, removedBefore = 0): number | null {
  const { visibleEnd, scrollEnd } = scrollContainerEnds(popup);
  const popupRect = popup.getBoundingClientRect();
  // Ends inside the visible area, or short of content that keeps the scrollable area as it is.
  if (popupRect.bottom < visibleEnd - 0.5 || popupRect.bottom < scrollEnd - 1) return null;
  const listRect = listbox.getBoundingClientRect();
  const frameBelow = popupRect.bottom - listRect.bottom;
  const hold = Math.min(listRect.height + removedBefore,
    Math.max(0, Math.ceil(visibleEnd - frameBelow - (listRect.top - removedBefore))));
  return hold > 0 ? hold : null;
}

/**
 * The viewport y of the visible end and of the scrollable end of the nearest scroll container
 * around `element`, or of the page.
 */
function scrollContainerEnds(element: HTMLElement): { visibleEnd: number; scrollEnd: number } {
  for (let node = element.parentElement; node; node = node.parentElement) {
    if (node === document.documentElement || node === document.body) break;
    const overflow = getComputedStyle(node).overflowY;
    if (overflow === 'auto' || overflow === 'scroll' || overflow === 'overlay') {
      const top = node.getBoundingClientRect().top + node.clientTop;
      return { visibleEnd: top + node.clientHeight, scrollEnd: top + node.scrollHeight - node.scrollTop };
    }
  }
  const page = document.scrollingElement ?? document.documentElement;
  return { visibleEnd: document.documentElement.clientHeight, scrollEnd: page.scrollHeight - page.scrollTop };
}
