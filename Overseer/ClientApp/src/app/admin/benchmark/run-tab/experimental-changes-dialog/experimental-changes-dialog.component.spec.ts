import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AdminBenchmarkService, BenchmarkExperimentalChangeCatalogDto } from '../../../../services/admin-benchmark.service';
import {
  EXPERIMENTAL_CHANGES_DIALOG_VIEW_KEY,
  EXPERIMENTAL_CHANGES_RUNS_DEBOUNCE_MS,
  ExperimentalChangesDialogComponent,
  experimentalChangesIo,
  toolParametersOf
} from './experimental-changes-dialog.component';
import { ExperimentalChangeEditorRequest } from '../experimental-changes';
import {
  askerCharacterProcedure,
  buildCatalog,
  builtInChanges,
  citePageFirst
} from '../experimental-changes.testing';

const settle = (ms = 0): Promise<void> => new Promise<void>(resolve => setTimeout(resolve, ms));

describe('ExperimentalChangesDialogComponent', () => {
  let fixture: ComponentFixture<ExperimentalChangesDialogComponent>;
  let component: ExperimentalChangesDialogComponent;
  let service: {
    getExperimentalChangeRuns: Mock;
    deleteExperimentalChange: Mock;
    setExperimentalChangeRetired: Mock;
    downloadExperimentalChangeFolder: Mock;
  };

  const dialog = (): HTMLDialogElement => fixture.nativeElement.querySelector('#experimentalChangesDialog');
  const q = <T extends Element = HTMLElement>(selector: string): T | null => dialog().querySelector<T>(selector);
  const card = (id: string): HTMLElement => dialog().querySelector<HTMLElement>(`.xc-card[data-change-id="${id}"]`)!;
  const check = (id: string): HTMLInputElement => card(id).querySelector<HTMLInputElement>('input.xc-check')!;
  const click = (el: Element | null): void => {
    (el as HTMLElement).click();
    fixture.detectChanges();
  };
  const clean = (el: Element | null): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const closed = (): Promise<void> => new Promise(resolve => dialog().addEventListener('close', () => resolve(), { once: true }));
  const actions = (id: string): string[] =>
    Array.from(card(id).querySelectorAll('.gh-action-popover-item')).map(item => clean(item));

  function clearView(): void {
    try {
      localStorage.removeItem(EXPERIMENTAL_CHANGES_DIALOG_VIEW_KEY);
    } catch {
      // Storage unavailable in this browser.
    }
  }

  function setCatalog(catalog: BenchmarkExperimentalChangeCatalogDto | null): void {
    fixture.componentRef.setInput('catalog', catalog);
    fixture.detectChanges();
  }

  function open(spec: string | null): void {
    component.open(spec);
    fixture.detectChanges();
  }

  beforeEach(async () => {
    clearView();
    service = {
      getExperimentalChangeRuns: vi.fn(() => of({ runCount: 0, olderHashRunCount: 0, runs: [] })),
      deleteExperimentalChange: vi.fn(() => of(undefined)),
      setExperimentalChangeRetired: vi.fn(() => of(undefined)),
      downloadExperimentalChangeFolder: vi.fn(() => of(new Blob(['zip'])))
    };
    await TestBed.configureTestingModule({
      imports: [ExperimentalChangesDialogComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: service }]
    }).compileComponents();
    fixture = TestBed.createComponent(ExperimentalChangesDialogComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('informationCondition', 'production');
    setCatalog(buildCatalog());
  });

  afterEach(() => {
    if (dialog()?.open) dialog().close();
    clearView();
    vi.restoreAllMocks();
  });

  describe('toolParametersOf', () => {
    it('should list each parameter with its type, whether it is required, and its description', () => {
      expect(toolParametersOf(JSON.stringify({
        type: 'object',
        properties: { race: { type: 'string', description: 'A race.' }, mode: { type: ['string', 'null'], enum: ['a', 'b'] } },
        required: ['race']
      }))).toEqual([
        { name: 'race', type: 'string', required: true, description: 'A race.' },
        { name: 'mode', type: 'string or null (one of a, b)', required: false, description: '' }
      ]);
      expect(toolParametersOf('{"type":"object"}')).toEqual([]);
      expect(toolParametersOf('{not json')).toBeNull();
    });
  });

  it('should open as a modal with the spec ticked and focused, built-in changes first, each by its title', () => {
    open('race-role-tool-v1');

    expect(dialog().open).toBe(true);
    expect(dialog().classList).not.toContain('gh-dialog-fullscreen');
    expect(Array.from(dialog().querySelectorAll('.xc-card .xc-card-title')).map(clean)).toEqual([
      'Answer from the player\'s own character', 'Race and role lookup tool', 'Search with the player\'s race and role',
      'Where the game code uses it', 'Cite the page first'
    ]);
    expect(check('race-role-tool-v1').checked).toBe(true);
    expect(document.activeElement).toBe(check('race-role-tool-v1'));
    expect(card('race-role-tool-v1').classList).toContain('is-checked');

    const race = card('race-role-tool-v1');
    expect(Array.from(race.querySelectorAll('.xc-kind')).map(clean)).toEqual(['New tool']);
    expect(clean(race.querySelector('.xc-expected'))).toContain('Should improve: Questions that depend');
    expect(clean(race.querySelector('.xc-used'))).toBe('Used in 3 runs');
    // The label is the title; the summary describes the checkbox.
    expect(clean(race.querySelector('label'))).toBe('Race and role lookup tool');
    expect(check('race-role-tool-v1').getAttribute('aria-describedby')).toBe('xc-summary-race-role-tool-v1');
    // No id outside Technical details.
    expect(clean(race)).not.toContain('race-role-tool-v1');

    expect(clean(card('cite-page-first-v1').querySelector('.xc-source'))).toMatch(/^Written in Overseer · edited \d+ Oct 2026$/);
    expect(clean(q('#xcStatus'))).toBe('1 change selected');
    expect(q('#xcStatus')!.getAttribute('aria-live')).toBe('polite');
    expect(q('#xcUseThis')!.getAttribute('aria-disabled')).toBeNull();
  });

  it('should tick and untick, with the live status, This run and the comparison it makes', () => {
    open(null);
    expect(clean(q('#xcStatus'))).toBe('No changes — same as the live chat');
    expect(clean(q('#xcThisRunCount'))).toBe('No changes — this run uses exactly what the live chat uses.');
    expect(clean(q('#xcCompare'))).toContain('This is a normal run.');
    expect(q('#xcClearAll')).toBeNull();

    click(check('race-role-tool-v1'));
    click(check('stats-referenced-by-v1'));

    expect(clean(q('#xcStatus'))).toBe('2 changes selected');
    expect(clean(q('#xcThisRunCount'))).toBe('2 changes');
    expect(Array.from(dialog().querySelectorAll('.xc-this-list li')).map(clean))
      .toEqual(['Race and role lookup tool', 'Where the game code uses it']);
    expect(clean(q('#xcCompare'))).toContain('A run identical except for Experimental Changes: None.');
    expect(clean(q('#xcTogether'))).toBe('The effect will belong to these 2 changes together. To learn which one did it, test each alone later.');
    expect(clean(q('#xcTechnical'))).toContain('race-role-tool-v1+stats-referenced-by-v1');

    click(check('stats-referenced-by-v1'));
    expect(clean(q('#xcStatus'))).toBe('1 change selected');
    expect(q('#xcTogether')).toBeNull();
  });

  it('should disable a change that conflicts with a ticked one, with the reason linked to its checkbox', () => {
    open(null);
    click(check('cite-page-first-v1'));

    const words = check('asker-search-words-v1');
    expect(words.disabled).toBe(true);
    const ids = words.getAttribute('aria-describedby')!.split(' ');
    expect(ids).toContain('xc-line-asker-search-words-v1');
    expect(document.getElementById('xc-line-asker-search-words-v1')?.textContent)
      .toBe('Cannot run with Cite the page first: one replaces the wiki_search description the other adds to.');

    click(check('cite-page-first-v1'));
    expect(check('asker-search-words-v1').disabled).toBe(false);
  });

  it('should disable a change that has no effect under the Tool Access choice, and say what does not apply', () => {
    fixture.componentRef.setInput('informationCondition', 'closedBook');
    fixture.detectChanges();
    open(null);

    expect(check('race-role-tool-v1').disabled).toBe(true);
    expect(clean(card('race-role-tool-v1').querySelector('.xc-line'))).toBe('Has no effect under Closed book: the AI gets no tools there.');
    expect(check('asker-character-procedure-v1').disabled).toBe(false);

    dialog().close();
    fixture.componentRef.setInput('informationCondition', 'sourceOnly');
    fixture.detectChanges();
    open(null);
    expect(check('asker-search-words-v1').disabled).toBe(true);
    expect(clean(card('asker-search-words-v1').querySelector('.xc-line')))
      .toBe('Has no effect under Source code only: wiki_search is not offered there.');
    expect(check('race-role-tool-v1').disabled).toBe(false);
    expect(clean(q('#xcAccess'))).toBe('Tool Access: Source code only — everything applies.');
  });

  it('should warn about a mention of a change that is not ticked, and Add it', () => {
    open('asker-character-procedure-v1');

    const warning = q('.xc-mention')!;
    expect(clean(warning)).toContain('Answer from the player\'s own character mentions get_character_info, which this run does not offer.');
    click(warning.querySelector('.xc-add'));

    expect(check('race-role-tool-v1').checked).toBe(true);
    expect(q('.xc-mention')).toBeNull();
  });

  it('should offer the recent selections as Used before buttons, pressed when they equal the selection', () => {
    setCatalog(buildCatalog(undefined, {
      recentSelections: [
        {
          spec: 'race-role-tool-v1+stats-referenced-by-v1', changeIds: ['race-role-tool-v1', 'stats-referenced-by-v1'], runCount: 3,
          lastRunId: 415, lastStartedAtUtc: '2026-10-23T10:00:00Z'
        },
        {
          spec: builtInChanges().map(c => c.id).sort().join('+'), changeIds: builtInChanges().map(c => c.id), runCount: 1,
          lastRunId: 420, lastStartedAtUtc: '2026-10-24T10:00:00Z'
        },
        { spec: 'gone-v1', changeIds: ['gone-v1'], runCount: 2, lastRunId: 401, lastStartedAtUtc: '2026-10-20T10:00:00Z' }
      ]
    }));
    open(null);

    const buttons = Array.from(dialog().querySelectorAll<HTMLButtonElement>('#xcUsedBefore button'));
    expect(buttons.map(clean)).toEqual(['Race and role lookup tool + Where the game code uses it · 3 runs', 'All four · 1 run']);
    expect(buttons.map(b => b.getAttribute('aria-pressed'))).toEqual(['false', 'false']);

    click(buttons[0]);
    expect(check('race-role-tool-v1').checked).toBe(true);
    expect(check('stats-referenced-by-v1').checked).toBe(true);
    expect(Array.from(dialog().querySelectorAll('#xcUsedBefore button')).map(b => b.getAttribute('aria-pressed')))
      .toEqual(['true', 'false']);
  });

  it('should clear every tick with Clear all', () => {
    open('race-role-tool-v1+stats-referenced-by-v1');
    click(q('#xcClearAll'));

    expect(check('race-role-tool-v1').checked).toBe(false);
    expect(check('stats-referenced-by-v1').checked).toBe(false);
    expect(clean(q('#xcStatus'))).toBe('No changes — same as the live chat');
    expect(q('#xcClearAll')).toBeNull();
  });

  it('should emit the canonical spec on Use this, and close', async () => {
    const chosen: (string | null)[] = [];
    let closedCount = 0;
    component.chosen.subscribe(spec => chosen.push(spec));
    component.closed.subscribe(() => closedCount++);
    open(null);

    click(check('stats-referenced-by-v1'));
    click(check('race-role-tool-v1'));
    const wait = closed();
    click(q('#xcUseThis'));
    await wait;

    expect(chosen).toEqual(['race-role-tool-v1+stats-referenced-by-v1']);
    expect(dialog().open).toBe(false);
    expect(closedCount).toBe(1);
  });

  it('should close without a choice on Escape and on Cancel', async () => {
    const chosen: (string | null)[] = [];
    let closedCount = 0;
    component.chosen.subscribe(spec => chosen.push(spec));
    component.closed.subscribe(() => closedCount++);
    open('race-role-tool-v1');

    let wait = closed();
    const escape = dialog() as HTMLDialogElement & { requestClose?: () => void };
    if (typeof escape.requestClose === 'function') {
      escape.requestClose();
    } else {
      escape.dispatchEvent(new Event('cancel', { cancelable: true }));
      escape.close();
    }
    await wait;
    expect(dialog().open).toBe(false);

    open('race-role-tool-v1');
    wait = closed();
    click(q('#xcCancel'));
    await wait;

    expect(chosen).toEqual([]);
    expect(closedCount).toBe(2);
  });

  it('should refuse Use this, with the reason, while the stamp would be too long or the catalog failed', () => {
    setCatalog(buildCatalog(undefined, { maxStampLength: 40 }));
    open('race-role-tool-v1+stats-referenced-by-v1');

    const useThis = q('#xcUseThis')!;
    expect(useThis.getAttribute('aria-disabled')).toBe('true');
    expect(useThis.getAttribute('aria-describedby')).toBe('xcFooterNote');
    expect(clean(q('#xcFooterNote'))).toBe('Too many changes for one run: the run record holds 40 characters. Choose fewer.');
    const chosen: (string | null)[] = [];
    component.chosen.subscribe(spec => chosen.push(spec));
    click(useThis);
    expect(chosen).toEqual([]);

    dialog().close();
    fixture.componentRef.setInput('catalogError', 'the server did not answer.');
    setCatalog(null);
    open(null);
    expect(clean(q('#xcCatalogError'))).toContain('the server did not answer.');
    expect(q('#xcUseThis')!.getAttribute('aria-disabled')).toBe('true');
  });

  it('should open the tips on first use and remember when they are closed', () => {
    open(null);
    const tips = q<HTMLDetailsElement>('#xcTips')!;
    expect(tips.open).toBe(true);
    expect(clean(tips)).toContain('Confirm a winner on a second suite:');

    tips.open = false;
    tips.dispatchEvent(new Event('toggle'));
    expect(JSON.parse(localStorage.getItem(EXPERIMENTAL_CHANGES_DIALOG_VIEW_KEY)!)).toEqual({ version: 1, tipsOpen: false });

    dialog().close();
    open(null);
    expect(q<HTMLDetailsElement>('#xcTips')!.open).toBe(false);
  });

  it('should show exactly what the AI gets: the tool\'s parameters, the addition, the replacement\'s diff, the instructions', () => {
    open(null);

    const tool = card('race-role-tool-v1');
    expect(clean(tool.querySelector('details > summary'))).toBe('Show exactly what the AI gets');
    expect(Array.from(tool.querySelectorAll('tbody tr')).map(tr => Array.from(tr.querySelectorAll('td')).map(clean).join(' '))).toEqual([
      'race string Yes A race name.',
      'role string No A role name.'
    ]);
    expect(tool.querySelector('pre')!.getAttribute('tabindex')).toBe('0');

    const words = card('asker-search-words-v1');
    expect(clean(words.querySelector('.xc-effect-label'))).toBe('Added to the end of the wiki_search description');
    expect(words.querySelector('pre.xc-inserted')?.textContent).toBe('Put the race and role in the query.');
    expect(words.querySelector('.xc-whole pre')?.textContent)
      .toBe('Searches the GnollHack wiki.\nReturns page titles.\n\nPut the race and role in the query.');

    const cite = card('cite-page-first-v1');
    expect(clean(cite.querySelector('.xc-effect-label'))).toBe('Replaces the wiki_search description');
    expect(cite.querySelector('app-text-diff')).not.toBeNull();
    click(cite.querySelector('input[value="side"]'));
    expect(clean(cite)).toContain('Left: the live description. Right: this change\'s.');

    expect(card('asker-character-procedure-v1').querySelector('pre')?.textContent)
      .toBe('When the question names a race, read get_character_info.');
    expect(clean(card('stats-referenced-by-v1'))).toContain('places in the game\'s code that use them');
  });

  it('should list in More actions only the actions that apply to the source and lock state', () => {
    setCatalog(buildCatalog([
      ...builtInChanges().map(c => c.id === 'asker-character-procedure-v1' ? askerCharacterProcedure({ canCopy: true }) : c),
      citePageFirst(),
      citePageFirst({ id: 'cite-page-first-v2', title: 'Cite the page first, v2', locked: true, canDelete: false, canEditText: false, conflictsWith: [] })
    ]));
    open(null);

    expect(actions('race-role-tool-v1')).toEqual(['Download as folder']);
    expect(actions('asker-character-procedure-v1')).toEqual(['Copy as new version…', 'Download as folder']);
    expect(actions('cite-page-first-v1')).toEqual(['Edit…', 'Copy as new version…', 'Download as folder', 'Retire', 'Delete…']);
    expect(actions('cite-page-first-v2')).toEqual(['Edit…', 'Copy as new version…', 'Download as folder', 'Retire']);
    expect(clean(card('cite-page-first-v2').querySelector('.xc-source'))).toContain('Locked — used by a run');

    const trigger = card('cite-page-first-v1').querySelector<HTMLElement>('.xc-more-btn')!;
    expect(trigger.getAttribute('aria-label')).toBe('More actions for Cite the page first');
    expect(trigger.getAttribute('popovertarget')).toBe('xc-more-cite-page-first-v1');
  });

  it('should ask the host for the editor on New change, Edit… and Copy as new version…', () => {
    const requests: ExperimentalChangeEditorRequest[] = [];
    component.editRequested.subscribe(request => requests.push(request));
    open(null);

    expect(q('#xcNewChange')!.getAttribute('aria-haspopup')).toBe('dialog');
    click(q('#xcNewChange'));
    click(card('cite-page-first-v1').querySelector('.xc-action-edit'));
    click(card('cite-page-first-v1').querySelector('.xc-action-copy'));

    expect(requests.map(r => [r.mode, r.change?.id ?? null])).toEqual([
      ['new', null], ['edit', 'cite-page-first-v1'], ['copy', 'cite-page-first-v1']
    ]);

    // Closed without a save, focus goes back to the control that opened the editor.
    component.onEditorClosed();
    expect(document.activeElement).toBe(card('cite-page-first-v1').querySelector('.xc-more-btn'));
  });

  it('should tick a new change after a save and focus it, and focus More actions after an edit', () => {
    open(null);
    const added = citePageFirst({ id: 'name-the-page-v1', title: 'Name the page', conflictsWith: [] });
    setCatalog(buildCatalog([...builtInChanges(), citePageFirst(), added]));

    component.onChangeSaved({ change: added, created: true });
    fixture.detectChanges();
    expect(check('name-the-page-v1').checked).toBe(true);
    expect(document.activeElement).toBe(check('name-the-page-v1'));
    expect(clean(q('#xcActionStatus'))).toBe('Saved Name the page.');

    component.onChangeSaved({ change: citePageFirst(), created: false });
    fixture.detectChanges();
    expect(document.activeElement).toBe(card('cite-page-first-v1').querySelector('.xc-more-btn'));
    expect(check('cite-page-first-v1').checked).toBe(false);
  });

  it('should confirm a delete inline on the card, and delete on Delete', () => {
    let changed = 0;
    component.catalogChanged.subscribe(() => changed++);
    open('cite-page-first-v1');

    click(card('cite-page-first-v1').querySelector('.xc-action-delete'));
    const confirm = card('cite-page-first-v1').querySelector('.xc-confirm')!;
    expect(clean(confirm)).toMatch(/^Delete "Cite the page first"\?/);
    expect(Array.from(confirm.querySelectorAll('button')).map(clean)).toEqual(['Delete', 'Keep']);
    expect(document.activeElement).toBe(confirm.querySelector('#xc-keep-cite-page-first-v1'));

    click(confirm.querySelector('#xc-keep-cite-page-first-v1'));
    expect(card('cite-page-first-v1').querySelector('.xc-confirm')).toBeNull();
    expect(service.deleteExperimentalChange).not.toHaveBeenCalled();

    click(card('cite-page-first-v1').querySelector('.xc-action-delete'));
    click(card('cite-page-first-v1').querySelector('#xc-delete-cite-page-first-v1'));

    expect(service.deleteExperimentalChange).toHaveBeenCalledWith('cite-page-first-v1');
    expect(changed).toBe(1);
    expect(clean(q('#xcActionStatus'))).toBe('Deleted Cite the page first.');
    expect(clean(q('#xcStatus'))).toBe('No changes — same as the live chat');
  });

  it('should say why a delete failed', () => {
    service.deleteExperimentalChange.mockReturnValue(throwError(() => ({ status: 409, error: { error: 'Its text is locked.', reason: 'locked' } })));
    open(null);

    click(card('cite-page-first-v1').querySelector('.xc-action-delete'));
    click(card('cite-page-first-v1').querySelector('#xc-delete-cite-page-first-v1'));

    expect(clean(q('#xcActionError'))).toBe('Its text is locked.');
  });

  it('should retire a change, list retired ones under Show retired, and restore one', () => {
    let changed = 0;
    component.catalogChanged.subscribe(() => changed++);
    open(null);
    click(card('cite-page-first-v1').querySelector('.xc-action-retire'));
    expect(service.setExperimentalChangeRetired).toHaveBeenCalledWith('cite-page-first-v1', true);
    expect(changed).toBe(1);

    setCatalog(buildCatalog([
      ...builtInChanges().map(c => c.id === 'stats-referenced-by-v1' ? { ...c, retired: true } : c),
      citePageFirst({ retired: true })
    ]));
    fixture.detectChanges();

    expect(dialog().querySelector('.xc-card[data-change-id="cite-page-first-v1"]')).toBeNull();
    expect(clean(q('#xcRetired summary'))).toBe('Show retired (2)');
    expect(clean(q('#xcRetired li[data-change-id="stats-referenced-by-v1"]'))).toContain('Retired in the repository');
    click(q('#xcRetired li[data-change-id="cite-page-first-v1"] .xc-restore'));

    expect(service.setExperimentalChangeRetired).toHaveBeenCalledWith('cite-page-first-v1', false);
    expect(changed).toBe(2);
  });

  it('should download a change as its folder', () => {
    const saveBlob = vi.spyOn(experimentalChangesIo, 'saveBlob').mockReturnValue(undefined);
    open(null);

    click(card('race-role-tool-v1').querySelector('.xc-action-download'));

    expect(service.downloadExperimentalChangeFolder).toHaveBeenCalledWith('race-role-tool-v1');
    expect(saveBlob).toHaveBeenCalledTimes(1);
    expect(saveBlob.mock.calls[0][0]).toBe('race-role-tool-v1.zip');
  });

  it('should list the runs with exactly the selection once the ticks settle, and open one\'s report', async () => {
    service.getExperimentalChangeRuns.mockReturnValue(of({
      runCount: 12, olderHashRunCount: 1,
      runs: [
        {
          id: 415, startedAtUtc: '2026-10-23T10:00:00Z', suiteName: 'Gnoll Basics', modelDisplayName: 'Model One',
          informationCondition: 'production', status: 'Completed', spec: 'race-role-tool-v1', hashMatchesCurrent: true
        },
        {
          id: 420, startedAtUtc: '2026-10-24T10:00:00Z', suiteName: 'Gnoll Basics', modelDisplayName: 'Model One',
          informationCondition: 'production', status: 'Completed', spec: 'race-role-tool-v1', hashMatchesCurrent: false
        }
      ]
    }));
    const reports: number[] = [];
    component.openRunReport.subscribe(id => reports.push(id));
    open(null);
    click(check('race-role-tool-v1'));
    expect(service.getExperimentalChangeRuns).not.toHaveBeenCalled();

    await settle(EXPERIMENTAL_CHANGES_RUNS_DEBOUNCE_MS + 50);
    fixture.detectChanges();

    expect(service.getExperimentalChangeRuns).toHaveBeenCalledTimes(1);
    expect(service.getExperimentalChangeRuns).toHaveBeenCalledWith('race-role-tool-v1');
    const links = Array.from(dialog().querySelectorAll<HTMLButtonElement>('#xcRuns button'));
    expect(links.map(clean)).toEqual(['#415', '#420']);
    expect(links[0].getAttribute('aria-label')).toContain('Open the report of run #415');
    expect(clean(q('#xcRunsSummary'))).toBe('12 runs in all; the newest 2 are listed. 1 of them used an older version of the text.');

    const wait = closed();
    click(links[1]);
    await wait;
    expect(reports).toEqual([420]);
    expect(dialog().open).toBe(false);
  });
});
