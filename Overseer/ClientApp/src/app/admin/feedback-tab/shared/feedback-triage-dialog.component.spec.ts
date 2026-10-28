import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { FeedbackTriageDialogComponent, FeedbackTriageDialogOptions } from './feedback-triage-dialog.component';

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

const VERSION_ERROR = 'Fixed in version takes letters, digits, periods, plus and minus signs only, at most 64 characters.';

describe('FeedbackTriageDialogComponent', () => {
  let fixture: ComponentFixture<FeedbackTriageDialogComponent>;
  let component: FeedbackTriageDialogComponent;
  let host: HTMLElement;
  let submit: Mock;

  const q = <T extends HTMLElement = HTMLElement>(selector: string): T | null => host.querySelector<T>(selector);
  const dialog = (): HTMLDialogElement => q<HTMLDialogElement>('dialog.ftd-dialog')!;
  const status = (): HTMLSelectElement => q<HTMLSelectElement>('#tl-fb-1-triage-status')!;
  const fixed = (): HTMLInputElement => q<HTMLInputElement>('#tl-fb-1-triage-fixed')!;
  const note = (): HTMLTextAreaElement => q<HTMLTextAreaElement>('#tl-fb-1-triage-note')!;
  const resolution = (): HTMLTextAreaElement => q<HTMLTextAreaElement>('#tl-fb-1-triage-resolution')!;

  function options(overrides: Partial<FeedbackTriageDialogOptions> = {}): FeedbackTriageDialogOptions {
    return {
      idPrefix: 'tl-fb-1-triage',
      subject: 'Dilithium crystal',
      values: { status: 'new', note: '', resolution: '', fixedInVersion: '', resolvedVia: [] },
      statuses: [
        { value: 'new', label: 'New' }, { value: 'acknowledged', label: 'Acknowledged' },
        { value: 'resolved', label: 'Resolved' }, { value: 'dismissed', label: 'Dismissed' }
      ],
      statusHelp: 'A resolved issue that recurs becomes Regressed; a dismissed one stays dismissed.',
      limits: { note: 1000, resolution: 4000, fixedInVersion: 64 },
      fixedInVersionPattern: /^[0-9A-Za-z.+\-]{1,64}$/,
      fixedInVersionError: VERSION_ERROR,
      submit,
      ...overrides
    };
  }

  function open(overrides: Partial<FeedbackTriageDialogOptions> = {}): void {
    component.open(options(overrides));
    fixture.detectChanges();
  }

  function typeInto(input: HTMLInputElement | HTMLTextAreaElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }

  function selectValue(select: HTMLSelectElement, value: string): void {
    select.value = value;
    select.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
  }

  function click(selector: string): void {
    q<HTMLButtonElement>(selector)!.click();
    fixture.detectChanges();
  }

  /** What Escape does: a cancel event the page may refuse, then the close. */
  function escape(): void {
    const event = new Event('cancel', { cancelable: true });
    dialog().dispatchEvent(event);
    if (!event.defaultPrevented) dialog().close();
    fixture.detectChanges();
  }

  beforeEach(async () => {
    submit = vi.fn().mockReturnValue(of(undefined));
    await TestBed.configureTestingModule({ imports: [FeedbackTriageDialogComponent] }).compileComponents();
    fixture = TestBed.createComponent(FeedbackTriageDialogComponent);
    component = fixture.componentInstance;
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  });

  afterEach(() => {
    if (dialog().open) dialog().close();
    fixture.destroy();
  });

  it('opens modal, named by its heading, with the issue\'s values and focus on Status', () => {
    open({ values: { status: 'acknowledged', note: 'Seen', resolution: 'Added the page.', fixedInVersion: '1.4.3', resolvedVia: [] } });

    expect(dialog().open).toBe(true);
    expect(dialog().matches(':modal')).toBe(true);
    expect(textOf(q('#' + dialog().getAttribute('aria-labelledby')))).toBe('Triage issue');
    expect(textOf(q('.ftd-issue-subject'))).toBe('Dilithium crystal');
    expect(q('#' + dialog().getAttribute('aria-describedby'))!.contains(q('.ftd-issue-subject'))).toBe(true);
    expect(status().value).toBe('acknowledged');
    expect(Array.from(status().options).map(o => o.value)).toEqual(['new', 'acknowledged', 'resolved', 'dismissed']);
    expect(note().value).toBe('Seen');
    expect(resolution().value).toBe('Added the page.');
    expect(fixed().value).toBe('1.4.3');
    expect(document.activeElement).toBe(status());
  });

  it('shows the issue\'s details under its subject, a dot between them on screen and a comma for assistive technology', () => {
    open({ details: ['Missing information', 'Status: Regressed', '12 occurrences'] });

    const details = q('.ftd-issue-details')!;
    expect(textOf(details)).toBe('Missing information · , Status: Regressed · , 12 occurrences');
    expect(Array.from(details.querySelectorAll('[aria-hidden="true"]')).map(s => textOf(s))).toEqual(['·', '·']);
  });

  it('keeps Cancel and Save on one row at the end of the footer, with the Fixed in version and text fields in the body font', () => {
    open();

    const cancel = q('.ftd-cancel')!.getBoundingClientRect();
    const save = q('.ftd-save')!.getBoundingClientRect();
    expect(save.top).toBe(cancel.top);
    expect(save.left).toBeGreaterThan(cancel.right);
    expect(getComputedStyle(resolution()).fontFamily).toBe(getComputedStyle(q('.ftd-issue-subject')!).fontFamily);
    expect(getComputedStyle(fixed()).fontFamily).not.toBe(getComputedStyle(resolution()).fontFamily);
  });

  it('closes through the header\'s close button, with the same unsaved-changes guard as Cancel', () => {
    open();
    const close = q('.ftd-close')!;
    expect(close.getAttribute('aria-label')).toBe('Close triage');
    click('.ftd-close');
    expect(dialog().open).toBe(false);

    open();
    typeInto(note(), 'Half a thought');
    click('.ftd-close');
    expect(dialog().open).toBe(true);
    expect(q('.ftd-unsaved')!.classList.contains('is-shown')).toBe(true);
  });

  it('labels every field and points Status and Fixed in version at their (i) text', () => {
    open();

    expect(textOf(q('label[for="tl-fb-1-triage-status"]'))).toBe('Status');
    expect(textOf(q('label[for="tl-fb-1-triage-fixed"]'))).toBe('Fixed in version');
    expect(textOf(q('label[for="tl-fb-1-triage-note"]'))).toBe('Note');
    expect(textOf(q('label[for="tl-fb-1-triage-resolution"]'))).toBe('Resolution');
    expect(textOf(q('#' + status().getAttribute('aria-describedby')))).toBe(
      'A resolved issue that recurs becomes Regressed; a dismissed one stays dismissed.');
    expect(textOf(q('#' + fixed().getAttribute('aria-describedby')))).toContain('The Overseer version that has the fix');
    expect(q('.ftd-field-status app-info-tip button')!.getAttribute('aria-label')).toBe('About Status');
    expect(fixed().getAttribute('maxlength')).toBe('64');
    expect(note().getAttribute('maxlength')).toBe('1000');
    expect(resolution().getAttribute('maxlength')).toBe('4000');
    expect(note().classList.contains('gh-textarea-autosize')).toBe(true);
    expect(textOf(q('#' + note().getAttribute('aria-describedby')))).toBe('0 of 1,000 characters');
    typeInto(resolution(), 'Fixed');
    expect(textOf(q('#' + resolution().getAttribute('aria-describedby')))).toBe('5 of 4,000 characters');
  });

  it('opens a regressed issue on Choose a status, and refuses to save until one is chosen', () => {
    open({ values: { status: '', note: '', resolution: '', fixedInVersion: '', resolvedVia: [] } });

    expect(Array.from(status().options).map(o => o.value)).toEqual(['', 'new', 'acknowledged', 'resolved', 'dismissed']);
    expect(status().options[0].disabled).toBe(true);
    click('.ftd-save');

    expect(submit).not.toHaveBeenCalled();
    expect(textOf(q('.ftd-status-error'))).toBe('Choose a status.');
    expect(status().getAttribute('aria-invalid')).toBe('true');
    expect(status().getAttribute('aria-describedby')).toContain('tl-fb-1-triage-status-error');
    expect(document.activeElement).toBe(status());

    selectValue(status(), 'resolved');
    expect(q('.ftd-status-error')).toBeNull();
    expect(status().getAttribute('aria-invalid')).toBeNull();
  });

  it('refuses a malformed fixed-in version at its field, before sending anything', () => {
    open();
    typeInto(fixed(), '1.4 .1');
    click('.ftd-save');

    expect(submit).not.toHaveBeenCalled();
    expect(textOf(q('.ftd-fixed-error'))).toBe(VERSION_ERROR);
    expect(fixed().getAttribute('aria-invalid')).toBe('true');
    expect(fixed().getAttribute('aria-describedby')).toBe('tl-fb-1-triage-fixed-help tl-fb-1-triage-fixed-error');
    expect(document.activeElement).toBe(fixed());
    expect(dialog().open).toBe(true);
  });

  it('hands the trimmed values to submit, shows Saving… while it runs and closes when it completes', () => {
    const response = new Subject<void>();
    submit.mockReturnValue(response.asObservable());
    open();

    selectValue(status(), 'resolved');
    typeInto(note(), '  Wiki page coming ');
    typeInto(resolution(), ' Added the page. ');
    typeInto(fixed(), ' 1.4.3 ');
    click('.ftd-save');

    expect(submit).toHaveBeenCalledWith({ status: 'resolved', note: 'Wiki page coming', resolution: 'Added the page.', fixedInVersion: '1.4.3' });
    const save = q('.ftd-save')!;
    expect(save.getAttribute('aria-busy')).toBe('true');
    expect(textOf(save)).toBe('Saving…');

    click('.ftd-save');
    expect(submit).toHaveBeenCalledTimes(1);
    escape();
    expect(dialog().open).toBe(true);

    response.next();
    response.complete();
    fixture.detectChanges();
    expect(dialog().open).toBe(false);
  });

  it('saves on Enter in Fixed in version', () => {
    open();
    typeInto(fixed(), '1.4.3');
    q<HTMLFormElement>('form.ftd-form')!.requestSubmit();
    fixture.detectChanges();

    expect(submit).toHaveBeenCalledTimes(1);
    expect(dialog().open).toBe(false);
  });

  it('keeps the dialog, the values and the server\'s message when the save fails', () => {
    submit.mockReturnValue(throwError(() => new Error('The issue could not be saved: Unknown status.')));
    open();
    typeInto(note(), 'Kept');
    click('.ftd-save');

    expect(dialog().open).toBe(true);
    const error = q('.ftd-error')!;
    expect(error.getAttribute('role')).toBe('alert');
    expect(textOf(error)).toBe('The issue could not be saved: Unknown status.');
    expect(note().value).toBe('Kept');
    expect(q('.ftd-save')!.getAttribute('aria-busy')).toBeNull();
  });

  it('closes a form nobody changed on Escape and on Cancel', () => {
    open();
    escape();
    expect(dialog().open).toBe(false);

    open();
    click('.ftd-cancel');
    expect(dialog().open).toBe(false);
    expect(submit).not.toHaveBeenCalled();
  });

  it('asks before Escape throws changes away, without moving a button, and Discard changes closes', () => {
    open();
    typeInto(note(), 'Half a thought');
    const unsaved = q('.ftd-unsaved')!;
    const discard = q('.ftd-discard')!;
    expect(getComputedStyle(unsaved).visibility).toBe('hidden');
    expect(getComputedStyle(discard).visibility).toBe('hidden');
    const cancelBefore = q('.ftd-cancel')!.getBoundingClientRect();

    escape();

    expect(dialog().open).toBe(true);
    expect(getComputedStyle(unsaved).visibility).toBe('visible');
    expect(getComputedStyle(discard).visibility).toBe('visible');
    expect(textOf(unsaved)).toBe('You have unsaved changes.Discard changes');
    expect(document.activeElement).toBe(discard);
    expect(textOf(q('#' + discard.getAttribute('aria-describedby')))).toBe('You have unsaved changes.');
    const cancelAfter = q('.ftd-cancel')!.getBoundingClientRect();
    expect(cancelAfter.top).toBe(cancelBefore.top);
    expect(cancelAfter.left).toBe(cancelBefore.left);

    click('.ftd-discard');
    expect(dialog().open).toBe(false);
    expect(submit).not.toHaveBeenCalled();
  });

  it('closes a changed form on a second Escape, and asks again on Cancel', () => {
    open();
    typeInto(note(), 'Half a thought');
    escape();
    escape();
    expect(dialog().open).toBe(false);

    open();
    typeInto(note(), 'Again');
    click('.ftd-cancel');
    expect(dialog().open).toBe(true);
    expect(q('.ftd-unsaved')!.classList.contains('is-shown')).toBe(true);
  });

  it('opens fresh: a reopened form shows the stored values, not the last edit', () => {
    open();
    typeInto(note(), 'Not saved');
    click('.ftd-discard');
    open();
    expect(note().value).toBe('');
    expect(q('.ftd-unsaved')!.classList.contains('is-shown')).toBe(false);
  });

  it('gives every label row one height, so Status and Fixed in version start level', () => {
    open();
    dialog().style.width = '40rem';
    fixture.detectChanges();

    const heights = Array.from(host.querySelectorAll<HTMLElement>('.ftd-label-row')).map(r => r.getBoundingClientRect().height);
    expect(heights.length).toBe(4);
    expect(new Set(heights).size).toBe(1);
    const statusTop = status().getBoundingClientRect().top;
    const fixedTop = fixed().getBoundingClientRect().top;
    expect(fixedTop).toBe(statusTop);
    expect(fixed().getBoundingClientRect().left).toBeGreaterThan(status().getBoundingClientRect().left);
  });

  describe('Resolved via', () => {
    const group = (): HTMLFieldSetElement => q<HTMLFieldSetElement>('fieldset.ftd-resolved')!;
    const boxes = (): HTMLInputElement[] => Array.from(group().querySelectorAll<HTMLInputElement>('input[type="checkbox"]'));
    const box = (id: string): HTMLInputElement => q<HTMLInputElement>(`#tl-fb-1-triage-resolved-${id}`)!;

    function toggle(input: HTMLInputElement): void {
      input.click();
      fixture.detectChanges();
    }

    it('follows Fixed in version as a group named Resolved via, with six labeled checkboxes whose ids are only their values', () => {
      open();

      expect(fixed().compareDocumentPosition(group()) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(group().compareDocumentPosition(note()) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(textOf(q('#' + group().getAttribute('aria-labelledby')))).toBe('Resolved via');
      expect(textOf(q('#' + group().getAttribute('aria-describedby')))).toBe(
        'Where the fix went. GnollHack wiki and knowledge-base fixes go live when the server\'s copy is pulled; an Overseer release needs a deploy.');
      expect(group().querySelector('legend app-info-tip button')!.getAttribute('aria-label')).toBe('About Resolved via');
      expect(boxes().map(b => b.value)).toEqual(
        ['wiki', 'knowledge_base', 'overseer_release', 'gnollhack_release', 'server_configuration', 'other']);
      expect(boxes().map(b => textOf(b.closest('label')))).toEqual(
        ['GnollHack wiki', 'Knowledge base', 'Overseer release', 'GnollHack release', 'Server configuration', 'Other']);
      expect(boxes().every(b => b.closest('label')!.classList.contains('checkbox-label'))).toBe(true);
      expect(textOf(group())).not.toMatch(/knowledge_base|overseer_release|gnollhack_release|server_configuration/);
      expect(boxes().some(b => b.checked)).toBe(false);
    });

    it('checks the stored ids', () => {
      open({ values: { status: 'resolved', note: '', resolution: '', fixedInVersion: '', resolvedVia: ['knowledge_base', 'wiki'] } });

      expect(boxes().filter(b => b.checked).map(b => b.value)).toEqual(['wiki', 'knowledge_base']);
      expect(component.dirty).toBe(false);
    });

    it('lays the choices out in two columns where there is room, one where not', () => {
      open();
      dialog().style.width = '40rem';
      fixture.detectChanges();
      expect(box('knowledge_base').getBoundingClientRect().top).toBe(box('wiki').getBoundingClientRect().top);
      expect(box('knowledge_base').getBoundingClientRect().left).toBeGreaterThan(box('wiki').getBoundingClientRect().left);

      dialog().style.width = '18rem';
      fixture.detectChanges();
      expect(box('knowledge_base').getBoundingClientRect().top).toBeGreaterThan(box('wiki').getBoundingClientRect().top);
      expect(box('knowledge_base').getBoundingClientRect().left).toBe(box('wiki').getBoundingClientRect().left);
    });

    it('makes the form dirty while the choice differs from the stored one, so Escape asks first', () => {
      open({ values: { status: 'new', note: '', resolution: '', fixedInVersion: '', resolvedVia: ['wiki'] } });

      toggle(box('other'));
      expect(component.dirty).toBe(true);
      toggle(box('other'));
      expect(component.dirty).toBe(false);

      toggle(box('wiki'));
      expect(component.dirty).toBe(true);
      escape();
      expect(dialog().open).toBe(true);
      expect(q('.ftd-unsaved')!.classList.contains('is-shown')).toBe(true);
    });

    it('sends no resolvedVia while the choice is unchanged', () => {
      open({ values: { status: 'resolved', note: '', resolution: '', fixedInVersion: '', resolvedVia: ['wiki'] } });
      typeInto(note(), 'Seen');
      click('.ftd-save');

      expect(submit).toHaveBeenCalledTimes(1);
      expect(submit.mock.calls[0][0]).toEqual({ status: 'resolved', note: 'Seen', resolution: '', fixedInVersion: '' });
      expect(Object.hasOwn(submit.mock.calls[0][0] as object, 'resolvedVia')).toBe(false);
    });

    it('sends the changed choice in the canonical order', () => {
      open({ values: { status: 'resolved', note: '', resolution: '', fixedInVersion: '', resolvedVia: ['wiki'] } });
      toggle(box('other'));
      toggle(box('overseer_release'));
      click('.ftd-save');

      expect(submit).toHaveBeenCalledWith({
        status: 'resolved', note: '', resolution: '', fixedInVersion: '', resolvedVia: ['wiki', 'overseer_release', 'other']
      });
    });

    it('sends an empty list when every stored choice is unchecked', () => {
      open({ values: { status: 'dismissed', note: '', resolution: '', fixedInVersion: '', resolvedVia: ['wiki', 'knowledge_base'] } });
      toggle(box('wiki'));
      toggle(box('knowledge_base'));
      click('.ftd-save');

      expect(submit).toHaveBeenCalledWith({ status: 'dismissed', note: '', resolution: '', fixedInVersion: '', resolvedVia: [] });
    });

    it('opens fresh: a reopened form shows the stored choice, not the last edit', () => {
      open();
      toggle(box('wiki'));
      click('.ftd-discard');
      open();

      expect(box('wiki').checked).toBe(false);
      expect(component.dirty).toBe(false);
    });
  });
});
