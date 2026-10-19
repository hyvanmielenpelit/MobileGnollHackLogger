import { scrollOptionIntoListbox } from './listbox-scroll.util';

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
