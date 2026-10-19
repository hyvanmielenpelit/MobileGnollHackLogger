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
