import { scrollOptionIntoListbox, scrollSafeListHold } from './listbox-scroll.util';

describe('scrollOptionIntoListbox', () => {
  let outer: HTMLElement;
  let listbox: HTMLElement;
  let options: HTMLElement[];

  beforeEach(() => {
    // A 100 px scroller holding a 300 px page, with a 90 px listbox of 20 px options near its end.
    outer = document.createElement('div');
    outer.style.cssText = 'height: 100px; overflow-y: auto;';
    const page = document.createElement('div');
    page.style.cssText = 'height: 300px; padding-top: 200px; box-sizing: border-box;';
    listbox = document.createElement('div');
    listbox.style.cssText = 'height: 90px; overflow-y: auto; border: 2px solid; box-sizing: border-box;';
    options = Array.from({ length: 10 }, () => {
      const option = document.createElement('div');
      option.style.cssText = 'height: 20px;';
      listbox.appendChild(option);
      return option;
    });
    page.appendChild(listbox);
    outer.appendChild(page);
    document.body.appendChild(outer);
    outer.scrollTop = 150;
  });

  afterEach(() => outer.remove());

  it('scrolls an option below the view up to the listbox bottom edge', () => {
    scrollOptionIntoListbox(listbox, options[6]);
    // Options 0–6 end at 140 px; the padding box is 86 px tall.
    expect(listbox.scrollTop).toBe(140 - 86);
    expect(outer.scrollTop).toBe(150);
  });

  it('scrolls an option above the view down to the listbox top edge', () => {
    listbox.scrollTop = 100;
    scrollOptionIntoListbox(listbox, options[2]);
    expect(listbox.scrollTop).toBe(40);
    expect(outer.scrollTop).toBe(150);
  });

  it('leaves an option already shown alone', () => {
    listbox.scrollTop = 20;
    scrollOptionIntoListbox(listbox, options[2]);
    expect(listbox.scrollTop).toBe(20);
    expect(outer.scrollTop).toBe(150);
  });

  it('never scrolls the outer container, even when the listbox is outside its view', () => {
    outer.scrollTop = 0;
    scrollOptionIntoListbox(listbox, options[9]);
    expect(listbox.scrollTop).toBe(200 - 86);
    expect(outer.scrollTop).toBe(0);
  });
});

describe('scrollSafeListHold', () => {
  let root: HTMLElement;

  /** A 300 px popup (10 px frame, 290 px listbox) placed `top` px into a positioned box inside `parent`. */
  function popupAt(parent: HTMLElement, top: number): { popup: HTMLElement; listbox: HTMLElement } {
    const anchor = document.createElement('div');
    anchor.style.cssText = 'position: relative; height: 0;';
    const popup = document.createElement('div');
    popup.style.cssText = `position: absolute; top: ${top}px; left: 0; width: 100px; padding: 6px 0 4px; box-sizing: border-box;`;
    const listbox = document.createElement('div');
    listbox.style.cssText = 'height: 290px; box-sizing: border-box;';
    popup.appendChild(listbox);
    anchor.appendChild(popup);
    parent.appendChild(anchor);
    return { popup, listbox };
  }

  beforeEach(() => {
    root = document.createElement('div');
    document.body.appendChild(root);
  });

  afterEach(() => root.remove());

  it('is null for a popup that ends inside its scroller', () => {
    const scroller = document.createElement('div');
    scroller.style.cssText = 'height: 400px; overflow-y: auto;';
    root.appendChild(scroller);
    const { popup, listbox } = popupAt(scroller, 20);
    expect(scrollSafeListHold(popup, listbox)).toBeNull();
  });

  it('keeps a popup that extends its scroller reaching the scroller\'s visible end', () => {
    const scroller = document.createElement('div');
    scroller.style.cssText = 'height: 200px; overflow-y: auto;';
    const content = document.createElement('div');
    content.style.cssText = 'height: 220px;';
    scroller.appendChild(content);
    root.appendChild(scroller);
    const { popup, listbox } = popupAt(scroller, 0);
    scroller.scrollTop = scroller.scrollHeight - scroller.clientHeight;
    expect(scroller.scrollTop).toBeGreaterThan(0);

    const hold = scrollSafeListHold(popup, listbox);
    expect(hold).not.toBeNull();
    const p = popup.getBoundingClientRect();
    const l = listbox.getBoundingClientRect();
    const s = scroller.getBoundingClientRect();
    const visibleEnd = s.top + scroller.clientTop + scroller.clientHeight;
    expect(Math.abs(l.top + hold! + (p.bottom - l.bottom) - visibleEnd)).toBeLessThanOrEqual(1);
  });

  it('is null for a popup that reaches past the visible end but not past the scroller\'s content', () => {
    const scroller = document.createElement('div');
    scroller.style.cssText = 'height: 200px; overflow-y: auto;';
    const content = document.createElement('div');
    content.style.cssText = 'height: 600px;';
    scroller.appendChild(content);
    root.appendChild(scroller);
    const { popup, listbox } = popupAt(content, 100);
    expect(popup.getBoundingClientRect().bottom).toBeGreaterThan(scroller.getBoundingClientRect().bottom);
    expect(scrollSafeListHold(popup, listbox)).toBeNull();
  });

  it('measures against the window when no ancestor scrolls', () => {
    const viewport = document.documentElement.clientHeight;
    const spacer = document.createElement('div');
    spacer.style.cssText = `height: ${viewport - 100 - root.getBoundingClientRect().top}px;`;
    root.appendChild(spacer);
    const past = popupAt(root, 0);
    expect(past.popup.getBoundingClientRect().bottom).toBeGreaterThan(viewport);
    expect(scrollSafeListHold(past.popup, past.listbox)).not.toBeNull();

    spacer.style.height = '0';
    const inside = popupAt(root, 0);
    past.popup.parentElement!.remove();
    expect(inside.popup.getBoundingClientRect().bottom).toBeLessThan(viewport);
    expect(scrollSafeListHold(inside.popup, inside.listbox)).toBeNull();
  });
});
