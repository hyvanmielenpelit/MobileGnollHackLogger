import type { Mock } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import {
  AdminBenchmarkService,
  BenchmarkInstructionVariantDetailsDto,
  BenchmarkInstructionVariantDto,
  BenchmarkInstructionVariantFeatureDetailsDto,
  BenchmarkInstructionVariantFeatureDto
} from '../../../../services/admin-benchmark.service';
import { diffLines } from '../../../../utils/text-diff.util';
import {
  INSTRUCTION_VARIANT_DIALOG_VIEW_KEY,
  InstructionVariantDialogComponent,
  productionGuideDrifted,
  toolParametersOf
} from './instruction-variant-dialog.component';

const TOOL: BenchmarkInstructionVariantFeatureDto = {
  id: 'tool.get_character_info', kind: 'experimentalTool', target: 'get_character_info', fromParent: true,
  appliesUnder: { production: true, sourceOnly: true, closedBook: false }, mentions: []
};
const WIKI: BenchmarkInstructionVariantFeatureDto = {
  id: 'guide.wiki_search', kind: 'rewordedGuide', target: 'wiki_search', fromParent: false,
  appliesUnder: { production: true, sourceOnly: false, closedBook: false }, mentions: []
};
const APPENDIX: BenchmarkInstructionVariantFeatureDto = {
  id: 'appendix.asker-profile-v1', kind: 'appendix', target: 'asker-profile-v1', fromParent: false,
  appliesUnder: { production: true, sourceOnly: true, closedBook: true }, mentions: ['tool.get_character_info']
};

const ASKER: BenchmarkInstructionVariantDto = {
  name: 'asker-profile-v1', description: 'Asker profile procedure.', extends: 'source-tools-v1', hashPrefix: 'ab12cd34ef56',
  isValid: true, experimentalTools: ['get_character_info'], experiments: [], rewordedTools: ['wiki_search'],
  hasPromptAppendix: true, rewordingsWithheldUnderSourceOnly: ['wiki_search'], features: [TOOL, WIKI, APPENDIX]
};
const BROKEN: BenchmarkInstructionVariantDto = {
  name: 'broken-v1', isValid: false, invalidReason: 'Its variant.json does not parse.', features: []
};

const LIVE_WIKI = 'Searches the GnollHack wiki.\nReturns page titles.';

function details(overrides: { productionText?: string } = {}): BenchmarkInstructionVariantDetailsDto {
  const features: BenchmarkInstructionVariantFeatureDetailsDto[] = [
    {
      ...TOOL, text: 'Looks up a race or a role.',
      parameterSchemaJson: JSON.stringify({
        type: 'object',
        properties: {
          race: { type: 'string', description: 'A race name.' },
          role: { type: 'string', description: 'A role name.' }
        },
        required: ['race']
      })
    },
    { ...WIKI, text: `${LIVE_WIKI}\nPut the race and role in the query.`, productionText: overrides.productionText ?? LIVE_WIKI },
    { ...APPENDIX, text: 'When the question names a race, read get_character_info.' }
  ];
  return {
    name: 'asker-profile-v1', description: 'Asker profile procedure.', parent: 'source-tools-v1',
    stamp: 'asker-profile-v1:ab12cd34ef56', isValid: true, invalidReason: null, features,
    runs: [{
      id: 41, startedAtUtc: '2026-10-20T10:00:00Z', suiteName: 'Default Suite', modelDisplayName: 'Model One',
      informationCondition: 'sourceOnly', status: 'Completed', spec: 'asker-profile-v1/minus/guide.wiki_search',
      hashMatchesCurrent: false
    }],
    runCount: 1, olderHashRunCount: 1
  };
}

describe('InstructionVariantDialogComponent', () => {
  let fixture: ComponentFixture<InstructionVariantDialogComponent>;
  let component: InstructionVariantDialogComponent;
  let getDetails: Mock;

  const dialog = (): HTMLDialogElement => fixture.nativeElement.querySelector('#instructionVariantDialog');
  const q = <T extends Element = HTMLElement>(selector: string): T | null => dialog().querySelector<T>(selector);
  const radio = (name: string, value: string): HTMLInputElement =>
    dialog().querySelector<HTMLInputElement>(`input[name="${name}"][value="${value}"]`)!;
  const click = (el: Element | null): void => {
    (el as HTMLElement).click();
    fixture.detectChanges();
  };
  const closed = (): Promise<void> => new Promise(resolve => dialog().addEventListener('close', () => resolve(), { once: true }));
  const clean = (el: Element | null): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

  function clearView(): void {
    try {
      localStorage.removeItem(INSTRUCTION_VARIANT_DIALOG_VIEW_KEY);
    } catch {
      // Storage unavailable in this browser.
    }
  }

  beforeEach(async () => {
    clearView();
    getDetails = vi.fn(() => of(details()));
    await TestBed.configureTestingModule({
      imports: [InstructionVariantDialogComponent],
      providers: [{ provide: AdminBenchmarkService, useValue: { getInstructionVariantDetails: getDetails } }]
    }).compileComponents();
    fixture = TestBed.createComponent(InstructionVariantDialogComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('variants', [ASKER, BROKEN]);
    fixture.componentRef.setInput('informationCondition', 'production');
    fixture.detectChanges();
  });

  afterEach(() => {
    if (dialog()?.open) dialog().close();
    clearView();
  });

  function open(spec: string | null): void {
    component.open(spec);
    fixture.detectChanges();
  }

  describe('productionGuideDrifted', () => {
    it('should not flag a variant that only adds lines to the live text', () => {
      expect(productionGuideDrifted(diffLines(LIVE_WIKI, `${LIVE_WIKI}\nOne more sentence.`))).toBe(false);
    });

    it('should flag a live text that gained or changed a line the copy lacks', () => {
      expect(productionGuideDrifted(diffLines(`${LIVE_WIKI}\nA new live line.`, `${LIVE_WIKI}\nOne more sentence.`))).toBe(true);
      expect(productionGuideDrifted(diffLines('Searches the wiki, newly worded.\nReturns page titles.', `${LIVE_WIKI}\nAdded.`))).toBe(true);
    });

    it('should ignore blank lines and line endings', () => {
      expect(productionGuideDrifted(diffLines(`${LIVE_WIKI}\n\n`, `${LIVE_WIKI}\nAdded.`))).toBe(false);
      expect(productionGuideDrifted(diffLines(LIVE_WIKI.replace('\n', '\r\n'), `${LIVE_WIKI}\nAdded.`))).toBe(false);
    });
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
    });

    it('should give no rows for a schema without properties, and null for one that does not parse', () => {
      expect(toolParametersOf('{"type":"object"}')).toEqual([]);
      expect(toolParametersOf('{not json')).toBeNull();
    });
  });

  it('should open on None, focus it, and list the valid variants as cards and the invalid ones disabled with their reason', () => {
    open(null);

    expect(dialog().open).toBe(true);
    expect(dialog().classList).toContain('gh-dialog-fullscreen');
    const none = dialog().querySelector<HTMLInputElement>('input[name="ivVariant"]:checked')!;
    expect(document.activeElement).toBe(none);
    expect(clean(none.closest('label'))).toContain('None — same as the live chat');

    const radios = Array.from(dialog().querySelectorAll<HTMLInputElement>('input[name="ivVariant"]'));
    expect(radios.length).toBe(3);
    const asker = clean(radios[1].closest('label'));
    expect(asker).toContain('asker-profile-v1');
    expect(asker).toContain('new tools and extra instructions');
    expect(asker).toContain('ab12cd34ef56');
    expect(asker).toContain('Builds on source-tools-v1');
    expect(asker).toContain('Used by 1 run');
    expect(radios[2].disabled).toBe(true);
    const reason = document.getElementById(radios[2].getAttribute('aria-describedby')!);
    expect(reason?.textContent).toContain('Its variant.json does not parse.');

    expect(getDetails).toHaveBeenCalledTimes(1);
    expect(getDetails).toHaveBeenCalledWith('asker-profile-v1');
    expect(q('#ivUseThis')!.getAttribute('aria-disabled')).toBeNull();
    expect(q('#ivFooterNote')).toBeNull();
  });

  it('should open About on first use and remember when it is closed', () => {
    open(null);
    const about = q<HTMLDetailsElement>('#ivAbout')!;
    expect(about.open).toBe(true);
    expect(about.textContent).toContain('One ablation at a time, and confirm a winner on a second suite.');

    about.open = false;
    about.dispatchEvent(new Event('toggle'));
    expect(JSON.parse(localStorage.getItem(INSTRUCTION_VARIANT_DIALOG_VIEW_KEY)!)).toEqual({ version: 1, aboutOpen: false });

    dialog().close();
    open(null);
    expect(q<HTMLDetailsElement>('#ivAbout')!.open).toBe(false);
  });

  it('should choose a whole variant with Use this, and emit closed', async () => {
    const chosen: (string | null)[] = [];
    let closedCount = 0;
    component.chosen.subscribe(spec => chosen.push(spec));
    component.closed.subscribe(() => closedCount++);
    open(null);

    click(radio('ivVariant', 'asker-profile-v1'));
    expect(clean(q('#ivDetailTitle'))).toBe('asker-profile-v1');
    expect(clean(q('#ivResult'))).toContain('Compare with a run without Experimental Changes that is identical otherwise.');

    const wait = closed();
    click(q('#ivUseThis'));
    await wait;

    expect(chosen).toEqual(['asker-profile-v1']);
    expect(dialog().open).toBe(false);
    expect(closedCount).toBe(1);
  });

  it('should leave one feature out, warn about a kept text that names it, and wait for the acknowledgment', () => {
    const chosen: (string | null)[] = [];
    component.chosen.subscribe(spec => chosen.push(spec));
    open('asker-profile-v1');

    click(radio('ivMode', 'minus'));
    expect(q('#ivUseThis')!.getAttribute('aria-disabled')).toBe('true');
    expect(q('#ivUseThis')!.getAttribute('aria-describedby')).toBe('ivFooterNote');
    expect(clean(q('#ivFooterNote'))).toBe('Choose the feature to leave out.');
    click(q('#ivUseThis'));
    expect(chosen).toEqual([]);

    click(radio('ivFeature', 'tool.get_character_info'));
    expect(clean(q('#ivResult'))).toContain('asker-profile-v1/minus/tool.get_character_info');
    expect(clean(q('#ivResult'))).toContain('asker-profile-v1 without the get_character_info tool');
    expect(clean(q('#ivResult'))).toContain('Compare with a run of asker-profile-v1 that is identical otherwise.');
    expect(clean(q('#ivDangling'))).toContain('the model will be told about a tool it does not have');
    expect(clean(q('#ivFooterNote'))).toBe('Confirm that the model may be told about something this run leaves out.');

    click(q('#ivDanglingAck'));
    expect(q('#ivUseThis')!.getAttribute('aria-disabled')).toBeNull();
    click(q('#ivUseThis'));

    expect(chosen).toEqual(['asker-profile-v1/minus/tool.get_character_info']);
  });

  it('should run only one feature, and refuse a feature that does not apply under the Tool Access choice', () => {
    fixture.componentRef.setInput('informationCondition', 'sourceOnly');
    fixture.detectChanges();
    open('asker-profile-v1');

    click(radio('ivMode', 'only'));
    const wiki = radio('ivFeature', 'guide.wiki_search');
    expect(wiki.disabled).toBe(true);
    expect(document.getElementById(wiki.getAttribute('aria-describedby')!)?.textContent)
      .toBe('Does not apply under Source code only: wiki_search is switched off there.');

    click(radio('ivFeature', 'appendix.asker-profile-v1'));
    expect(clean(q('#ivResult'))).toContain('asker-profile-v1, only the extra instructions of asker-profile-v1');
    expect(clean(q('#ivResult'))).toContain('Compare with a run without Experimental Changes that is identical otherwise.');
    // The appendix names get_character_info, which a single-feature run of it leaves out.
    expect(q('#ivDangling')).not.toBeNull();
  });

  it('should open on an ablation spec with its mode and feature checked', () => {
    open('asker-profile-v1/only/tool.get_character_info');

    expect(radio('ivVariant', 'asker-profile-v1').checked).toBe(true);
    expect(radio('ivMode', 'only').checked).toBe(true);
    expect(radio('ivFeature', 'tool.get_character_info').checked).toBe(true);
    expect(document.activeElement).toBe(radio('ivVariant', 'asker-profile-v1'));
  });

  it('should show what each feature changes: the tool\'s parameters, the reworded description\'s diff, the appendix text', () => {
    open('asker-profile-v1');

    const tool = q('[data-feature-id="tool.get_character_info"]')!;
    expect(clean(tool.querySelector('summary'))).toContain('Experimental tool: get_character_info');
    expect(clean(tool.querySelector('summary'))).toContain('From source-tools-v1');
    expect(Array.from(tool.querySelectorAll('tbody tr')).map(tr => Array.from(tr.querySelectorAll('td')).map(clean).join(' '))).toEqual([
      'race string Yes A race name.',
      'role string No A role name.'
    ]);

    const wiki = q('[data-feature-id="guide.wiki_search"]')!;
    expect(wiki.querySelector('.iv-drift')).toBeNull();
    expect(wiki.querySelector('app-text-diff')).not.toBeNull();
    expect(clean(wiki)).toContain('Removed lines are the live description\'s; added lines are this variant\'s.');
    click(wiki.querySelector('input[value="side"]'));
    expect(clean(wiki)).toContain('Left: the live description. Right: this variant\'s.');

    const appendix = q('[data-feature-id="appendix.asker-profile-v1"]')!;
    expect(appendix.querySelector('pre')?.textContent).toBe('When the question names a race, read get_character_info.');
  });

  it('should warn when the live description has lines the variant\'s copy does not have', () => {
    getDetails.mockReturnValue(of(details({ productionText: `${LIVE_WIKI}\nA line added to the live guide later.` })));
    open('asker-profile-v1');

    expect(clean(q('[data-feature-id="guide.wiki_search"] .iv-drift')))
      .toBe('The live description has changed since this variant copied it; a -v2 should re-base on it.');
  });

  it('should list the runs that used it, and open one\'s report', async () => {
    const reports: number[] = [];
    component.openRunReport.subscribe(id => reports.push(id));
    open('asker-profile-v1');

    expect(clean(q('#ivRunsSummary'))).toBe('1 run in all. 1 of them used an older version of the files.');
    const item = q('.iv-runs li')!;
    expect(clean(item)).toContain('asker-profile-v1 without the reworded wiki_search description');
    expect(clean(item)).toContain('Source code only');
    expect(clean(item)).toContain('Older version');

    const wait = closed();
    click(item.querySelector('button'));
    await wait;
    expect(reports).toEqual([41]);
    expect(dialog().open).toBe(false);
  });

  it('should say so when the details cannot be loaded', () => {
    getDetails.mockReturnValue(throwError(() => ({ status: 500 })));
    open('asker-profile-v1');

    expect(clean(q('.gh-field-error'))).toBe('What asker-profile-v1 changes could not be loaded.');
    // The catalog's features still allow an ablation choice.
    click(radio('ivMode', 'minus'));
    expect(radio('ivFeature', 'guide.wiki_search')).not.toBeNull();
  });

  it('should close without a choice on Cancel', async () => {
    const chosen: (string | null)[] = [];
    component.chosen.subscribe(spec => chosen.push(spec));
    open('asker-profile-v1');

    const wait = closed();
    click(q('#ivCancel'));
    await wait;

    expect(chosen).toEqual([]);
    expect(dialog().open).toBe(false);
  });
});
