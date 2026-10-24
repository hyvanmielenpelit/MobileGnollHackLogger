import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AdminBenchmarkService, BenchmarkExperimentalChangeEditRequest } from '../../../../services/admin-benchmark.service';
import {
  EXPERIMENTAL_CHANGE_SCREEN_DEBOUNCE_MS,
  ExperimentalChangeEditorDialogComponent
} from './experimental-change-editor-dialog.component';
import { ExperimentalChangeSaved } from '../experimental-changes';
import { LIVE_WIKI_SEARCH, buildCatalog, citePageFirst } from '../experimental-changes.testing';

const settle = (ms = 0): Promise<void> => new Promise<void>(resolve => setTimeout(resolve, ms));

describe('ExperimentalChangeEditorDialogComponent', () => {
  let fixture: ComponentFixture<ExperimentalChangeEditorDialogComponent>;
  let component: ExperimentalChangeEditorDialogComponent;
  let service: {
    createExperimentalChange: Mock;
    updateExperimentalChange: Mock;
    screenExperimentalChangeTexts: Mock;
  };

  const dialog = (): HTMLDialogElement => fixture.nativeElement.querySelector('#experimentalChangeEditorDialog');
  const q = <T extends Element = HTMLElement>(selector: string): T | null => dialog().querySelector<T>(selector);
  const clean = (el: Element | null): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const click = (el: Element | null): void => {
    (el as HTMLElement).click();
    fixture.detectChanges();
  };
  const type = (el: Element | null, value: string): void => {
    const input = el as HTMLInputElement | HTMLTextAreaElement;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  };
  const choose = (el: Element | null, value: string): void => {
    const select = el as HTMLSelectElement;
    select.value = value;
    select.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
  };
  const blocks = (): HTMLFieldSetElement[] => Array.from(dialog().querySelectorAll<HTMLFieldSetElement>('fieldset.xce-block'));
  const closed = (): Promise<void> => new Promise(resolve => dialog().addEventListener('close', () => resolve(), { once: true }));
  const note = (): string => clean(q('#xceFooterNote'));

  beforeEach(async () => {
    service = {
      createExperimentalChange: vi.fn((req: BenchmarkExperimentalChangeEditRequest) => of({
        change: citePageFirst({ id: 'name-the-page-v1', title: req.title }), overlaps: []
      })),
      updateExperimentalChange: vi.fn(() => of({ change: citePageFirst(), overlaps: [] })),
      screenExperimentalChangeTexts: vi.fn(() => of({ overlaps: [] }))
    };
    await TestBed.configureTestingModule({
      imports: [ExperimentalChangeEditorDialogComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: service }]
    }).compileComponents();
    fixture = TestBed.createComponent(ExperimentalChangeEditorDialogComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('catalog', buildCatalog());
    fixture.detectChanges();
  });

  afterEach(() => {
    if (dialog()?.open) dialog().close();
  });

  function openNew(): void {
    component.open({ mode: 'new', change: null });
    fixture.detectChanges();
  }

  /** Title, summary, and an instructions block with text, all valid. */
  function fillValid(): void {
    type(q('#xceTitle'), 'Name the page');
    type(q('#xceSummary'), 'Asks the AI to name the page it read.');
    click(q('#xceAddInstructions'));
    type(blocks()[0].querySelector('textarea'), 'Name the page you read before you answer.');
  }

  it('should open a new change with Title focused, required fields with counts, and Save waiting for them', () => {
    openNew();

    expect(dialog().open).toBe(true);
    expect(clean(q('#xceHeading'))).toBe('New experimental change');
    expect(document.activeElement).toBe(q('#xceTitle'));
    const title = q<HTMLInputElement>('#xceTitle')!;
    expect(title.required).toBe(true);
    expect(title.getAttribute('maxlength')).toBe('60');
    expect(q<HTMLTextAreaElement>('#xceSummary')!.required).toBe(true);
    expect(q<HTMLTextAreaElement>('#xceSummary')!.getAttribute('maxlength')).toBe('220');
    expect(document.querySelector('label[for="xceTitle"]')).not.toBeNull();

    type(title, 'Cite the page');
    expect(clean(q('#xceTitleCount'))).toBe('13/60');
    expect(title.getAttribute('aria-describedby')).toBe('xceTitleCount');

    const save = q('#xceSave')!;
    expect(save.getAttribute('aria-disabled')).toBe('true');
    expect(save.getAttribute('aria-describedby')).toBe('xceFooterNote');
    expect(note()).toBe('Write a summary.');
  });

  it('should show the errors and focus the first invalid field when Save is refused', () => {
    openNew();
    click(q('#xceSave'));

    expect(service.createExperimentalChange).not.toHaveBeenCalled();
    expect(clean(q('#xceTitle-error'))).toBe('Write a title.');
    expect(q('#xceTitle')!.getAttribute('aria-invalid')).toBe('true');
    expect(q('#xceTitle')!.getAttribute('aria-describedby')).toBe('xceTitleCount xceTitle-error');
    expect(clean(q('#xceSummary-error'))).toBe('Write a summary.');
    expect(clean(q('#xceEffects-error'))).toBe('Add at least one change.');
    expect(document.activeElement).toBe(q('#xceTitle'));
  });

  it('should add and remove blocks, never offer a tool twice, and drop an Add button at its limit', () => {
    openNew();
    expect(clean(q('#xceAddButtons'))).toBe('Add to a tool description Replace a tool description Add instructions');

    click(q('#xceAddAddition'));
    expect(blocks().length).toBe(1);
    expect(clean(blocks()[0].querySelector('legend'))).toBe('Added to the end of a tool\'s description');
    const first = blocks()[0].querySelector<HTMLSelectElement>('select')!;
    expect(document.activeElement).toBe(first);
    expect(Array.from(first.options).map(o => o.textContent?.trim()))
      .toEqual(['Choose a tool', 'wiki_search (not offered under Source code only)', 'source_code_search']);
    choose(first, 'wiki_search');
    expect(clean(blocks()[0])).toContain('Not offered under Source code only, so this part does not reach the AI there.');

    click(q('#xceAddReplacement'));
    const second = blocks()[1].querySelector<HTMLSelectElement>('select')!;
    expect(Array.from(second.options).map(o => o.value)).toEqual(['', 'source_code_search']);
    choose(second, 'source_code_search');
    // Both production tools are used, so no further description block can be added.
    expect(q('#xceAddAddition')).toBeNull();
    expect(q('#xceAddReplacement')).toBeNull();

    click(q('#xceAddInstructions'));
    expect(blocks().length).toBe(3);
    expect(q('#xceAddInstructions')).toBeNull();

    click(blocks()[0].querySelector('.xce-remove'));
    expect(blocks().map(b => b.getAttribute('data-kind'))).toEqual(['replace', 'instructions']);
    expect(q('#xceAddAddition')).not.toBeNull();
  });

  it('should prefill a replacement with the live description and show the change as a diff', () => {
    openNew();
    click(q('#xceAddReplacement'));
    choose(blocks()[0].querySelector('select'), 'wiki_search');

    const text = blocks()[0].querySelector<HTMLTextAreaElement>('textarea')!;
    expect(text.value).toBe(LIVE_WIKI_SEARCH);
    expect(clean(blocks()[0].querySelector('label[for$="-text"]'))).toBe('The new description');

    type(text, `${LIVE_WIKI_SEARCH}\nCite the page first.`);
    expect(blocks()[0].querySelector('app-text-diff')).not.toBeNull();
    expect(clean(blocks()[0])).toContain('Cite the page first.');
  });

  it('should show what the AI will read for an addition', () => {
    openNew();
    click(q('#xceAddAddition'));
    choose(blocks()[0].querySelector('select'), 'wiki_search');
    type(blocks()[0].querySelector('textarea'), 'Read the page a result names.');

    const preview = blocks()[0].querySelector('.xce-preview')!;
    expect(clean(preview.querySelector('summary'))).toBe('What the AI will read');
    expect(preview.querySelector('pre')?.textContent).toBe(`${LIVE_WIKI_SEARCH}\n\nRead the page a result names.`);
    expect(preview.querySelector('pre ins')?.textContent).toBe('Read the page a result names.');
  });

  it('should screen the texts once typing pauses, and show each finding or that there is none', async () => {
    service.screenExperimentalChangeTexts.mockReturnValue(of({
      overlaps: [{
        text: 'the quest artifact of the healer is the staff', suiteId: 1, suiteName: 'Gnoll Basics', questionId: 9,
        questionNumber: 3, field: 'rubric'
      }]
    }));
    openNew();
    click(q('#xceAddInstructions'));
    type(blocks()[0].querySelector('textarea'), 'The quest artifact of the healer is the staff of Aesculapius.');
    expect(service.screenExperimentalChangeTexts).not.toHaveBeenCalled();

    await settle(EXPERIMENTAL_CHANGE_SCREEN_DEBOUNCE_MS + 50);
    fixture.detectChanges();

    expect(service.screenExperimentalChangeTexts).toHaveBeenCalledWith(['The quest artifact of the healer is the staff of Aesculapius.']);
    expect(Array.from(dialog().querySelectorAll('#xceOverlaps li')).map(clean))
      .toEqual(['Shares "the quest artifact of the healer is the staff" with the rubric of question 3 of Gnoll Basics.']);

    service.screenExperimentalChangeTexts.mockReturnValue(of({ overlaps: [] }));
    type(blocks()[0].querySelector('textarea'), 'Name the page you read.');
    await settle(EXPERIMENTAL_CHANGE_SCREEN_DEBOUNCE_MS + 50);
    fixture.detectChanges();
    expect(q('#xceOverlaps')).toBeNull();
    expect(clean(q('#xceNoOverlap'))).toBe('No overlap with any benchmark question or rubric.');
  });

  it('should untick the confirmation whenever the texts change, and wait for it', () => {
    openNew();
    fillValid();
    expect(note()).toBe('Tick the confirmation that the text states no game fact, answer or hint.');

    click(q('#xceConfirm'));
    expect(q<HTMLInputElement>('#xceConfirm')!.checked).toBe(true);
    expect(q('#xceSave')!.getAttribute('aria-disabled')).toBeNull();
    expect(q('#xceFooterNote')).toBeNull();

    type(blocks()[0].querySelector('textarea'), 'Name the page you read first.');
    expect(q<HTMLInputElement>('#xceConfirm')!.checked).toBe(false);
    expect(q('#xceSave')!.getAttribute('aria-disabled')).toBe('true');

    // A display field does not untick it.
    click(q('#xceConfirm'));
    type(q('#xceExpected'), 'Fewer answers that cite no page');
    expect(q<HTMLInputElement>('#xceConfirm')!.checked).toBe(true);
  });

  it('should save a new change, emit it before closing, and report the close as a save', async () => {
    const saved: ExperimentalChangeSaved[] = [];
    const closes: boolean[] = [];
    component.saved.subscribe(s => saved.push(s));
    component.closed.subscribe(c => closes.push(c));
    openNew();
    fillValid();
    type(q('#xceExpected'), 'Fewer answers that cite no page');
    click(q('#xceConfirm'));

    const wait = closed();
    click(q('#xceSave'));
    await wait;

    expect(service.createExperimentalChange).toHaveBeenCalledWith({
      title: 'Name the page', summary: 'Asks the AI to name the page it read.', expectedEffect: 'Fewer answers that cite no page',
      descriptionEdits: [], instructions: 'Name the page you read before you answer.', copiedFrom: null, genericConfirmed: true
    });
    expect(saved.map(s => [s.change.id, s.created])).toEqual([['name-the-page-v1', true]]);
    expect(closes).toEqual([true]);
    expect(dialog().open).toBe(false);
  });

  it('should lock the texts of a change a run used, keep the display fields editable, and save without the confirmation', () => {
    const locked = citePageFirst({ locked: true, canEditText: false, canDelete: false });
    component.open({ mode: 'edit', change: locked });
    fixture.detectChanges();

    expect(clean(q('#xceHeading'))).toBe('Edit Cite the page first');
    expect(clean(q('#xceLocked'))).toContain('Used by a run, so its text is locked');
    const text = blocks()[0].querySelector<HTMLTextAreaElement>('textarea')!;
    expect(text.readOnly).toBe(true);
    expect(blocks()[0].querySelector<HTMLSelectElement>('select')!.disabled).toBe(true);
    expect(blocks()[0].querySelector('.xce-remove')).toBeNull();
    expect(q('#xceAddButtons')).toBeNull();
    expect(q('#xceConfirm')).toBeNull();

    type(q('#xceSummary'), 'Asks for the page before the answer.');
    expect(q('#xceSave')!.getAttribute('aria-disabled')).toBeNull();
    click(q('#xceSave'));

    expect(service.updateExperimentalChange).toHaveBeenCalledWith('cite-page-first-v1', expect.objectContaining({
      summary: 'Asks for the page before the answer.', genericConfirmed: false, expectedUpdatedAtUtc: '2026-10-24T10:00:00Z',
      descriptionEdits: [{ tool: 'wiki_search', mode: 'replace', text: `${LIVE_WIKI_SEARCH}\nCite the page first.` }]
    }));
  });

  it('should start a new version from the locked banner, copied from the change', () => {
    component.open({ mode: 'edit', change: citePageFirst({ locked: true, canEditText: false }) });
    fixture.detectChanges();
    click(q('#xceCopyVersion'));

    expect(clean(q('#xceHeading'))).toBe('New version of Cite the page first');
    expect(q('#xceLocked')).toBeNull();
    expect(blocks()[0].querySelector<HTMLTextAreaElement>('textarea')!.readOnly).toBe(false);
    click(q('#xceConfirm'));
    click(q('#xceSave'));

    expect(service.createExperimentalChange).toHaveBeenCalledWith(expect.objectContaining({ copiedFrom: 'cite-page-first-v1', genericConfirmed: true }));
  });

  it('should ask before discarding edits on Cancel and on Escape', async () => {
    const closes: boolean[] = [];
    component.closed.subscribe(c => closes.push(c));
    openNew();
    type(q('#xceTitle'), 'Half a title');

    click(q('#xceCancel'));
    expect(dialog().open).toBe(true);
    expect(clean(q('#xceDiscard'))).toMatch(/^Discard your edits\?/);
    expect(Array.from(q('#xceDiscard')!.querySelectorAll('button')).map(clean)).toEqual(['Keep editing', 'Discard']);
    expect(document.activeElement).toBe(q('#xceKeepEditing'));
    click(q('#xceKeepEditing'));
    expect(q('#xceDiscard')).toBeNull();

    const escape = new Event('cancel', { cancelable: true });
    dialog().dispatchEvent(escape);
    fixture.detectChanges();
    expect(escape.defaultPrevented).toBe(true);
    expect(q('#xceDiscard')).not.toBeNull();

    const wait = closed();
    click(q('#xceDiscardEdits'));
    await wait;
    expect(closes).toEqual([false]);
  });

  it('should close at once without edits', async () => {
    openNew();
    const wait = closed();
    click(q('#xceCancel'));
    await wait;
    expect(dialog().open).toBe(false);
  });

  it('should show a 409 in the footer note with Reload, and start again on the change as it now stands', () => {
    service.updateExperimentalChange.mockReturnValue(throwError(() => ({
      status: 409, error: { error: 'The change was edited elsewhere. Reload it and edit again.', reason: 'editedElsewhere' }
    })));
    const reloads: string[] = [];
    component.reloadRequested.subscribe(id => reloads.push(id));
    component.open({ mode: 'edit', change: citePageFirst() });
    fixture.detectChanges();
    type(q('#xceExpected'), 'Fewer answers without a page');
    click(q('#xceSave'));

    expect(note()).toMatch(/^The change was edited elsewhere\. Reload it and edit again\./);
    expect(Array.from(q('#xceFooterNote')!.querySelectorAll('button')).map(clean)).toEqual(['Reload']);
    expect(q('#xceSave')!.getAttribute('aria-disabled')).toBe('true');
    click(q('#xceReload'));
    expect(reloads).toEqual(['cite-page-first-v1']);

    component.reloadFrom(buildCatalog([citePageFirst({ summary: 'Edited elsewhere.', updatedAtUtc: '2026-10-24T11:00:00Z' })]));
    fixture.detectChanges();
    expect(q<HTMLTextAreaElement>('#xceSummary')!.value).toBe('Edited elsewhere.');
    expect(q('#xceReload')).toBeNull();
  });

  it('should put the server\'s field errors under their fields', () => {
    service.createExperimentalChange.mockReturnValue(throwError(() => ({
      status: 400, error: { error: 'The change is not valid.', fieldErrors: { 'descriptionEdits[0].text': 'The text is longer than 4,000 characters.' } }
    })));
    openNew();
    type(q('#xceTitle'), 'Name the page');
    type(q('#xceSummary'), 'Asks the AI to name the page it read.');
    click(q('#xceAddAddition'));
    choose(blocks()[0].querySelector('select'), 'wiki_search');
    type(blocks()[0].querySelector('textarea'), 'Name the page.');
    click(q('#xceConfirm'));
    click(q('#xceSave'));

    const textId = blocks()[0].querySelector('textarea')!.id;
    expect(clean(document.getElementById(`${textId}-error`))).toBe('The text is longer than 4,000 characters.');
    expect(document.activeElement?.id).toBe(textId);
  });
});
