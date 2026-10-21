import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { AiTelemetryKeySource, AiTelemetrySeriesModelOptionDto } from '../../../../services/admin.service';
import { ChartModelRow, TL_CHARTS_MODELS_VIEW_STORAGE_KEY, modelRows } from './telemetry-chart-filters';
import { TelemetryChartModelsComponent } from './telemetry-chart-models.component';

const settle = (ms = 0): Promise<void> => new Promise<void>(resolve => setTimeout(resolve, ms));

const textOf = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

function option(index: number, overrides: Partial<AiTelemetrySeriesModelOptionDto> = {}): AiTelemetrySeriesModelOptionDto {
  return {
    provider: 'Anthropic', modelId: `model-${index}`, modelName: `Model ${index}`, keySources: ['system'],
    requests: 1000 - index, estimatedCost: 1, pricedRequests: 1000 - index, tokens: 1000, lastRequestUtc: '2026-10-12T21:15:00Z',
    ...overrides
  };
}

/** Models 1 to n, most requests first. */
function rowsOf(n: number, overrides: (i: number) => Partial<AiTelemetrySeriesModelOptionDto> = () => ({})): ChartModelRow[] {
  return modelRows({ models: Array.from({ length: n }, (_, i) => option(i + 1, overrides(i + 1))) });
}

function clearStorage(): void {
  try {
    localStorage.removeItem(TL_CHARTS_MODELS_VIEW_STORAGE_KEY);
  } catch {
    // Nothing stored.
  }
}

describe('TelemetryChartModelsComponent', () => {
  let fixture: ComponentFixture<TelemetryChartModelsComponent>;
  let component: TelemetryChartModelsComponent;
  let el: HTMLElement;
  let emitted: string[][];

  beforeEach(async () => {
    clearStorage();
    await TestBed.configureTestingModule({ imports: [TelemetryChartModelsComponent] }).compileComponents();
  });

  afterEach(() => {
    el?.querySelectorAll<HTMLElement>('[popover]').forEach(popover => {
      try { popover.hidePopover(); } catch { /* already closed */ }
    });
    fixture?.destroy();
    clearStorage();
  });

  function create(rows: ChartModelRow[], options: { selected?: string[]; max?: number; keys?: AiTelemetryKeySource } = {}): void {
    fixture = TestBed.createComponent(TelemetryChartModelsComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
    emitted = [];
    fixture.componentRef.setInput('rows', rows);
    fixture.componentRef.setInput('selectedKeys', options.selected ?? []);
    if (options.max !== undefined) fixture.componentRef.setInput('max', options.max);
    if (options.keys !== undefined) fixture.componentRef.setInput('keys', options.keys);
    // The dialog feeds the choice back, as the real host does.
    component.selectionChange.subscribe(keys => {
      emitted.push(keys);
      fixture.componentRef.setInput('selectedKeys', keys);
    });
    fixture.detectChanges();
  }

  const q = <T extends HTMLElement = HTMLElement>(selector: string): T | null => el.querySelector<T>(selector);
  const label = () => q('#tl-sel-models-label')!;
  const checkbox = (i: number) => q<HTMLInputElement>(`#tl-sel-models-opt-${i}`)!;
  const names = () => Array.from(el.querySelectorAll('.tl-sel-row-name')).map(textOf);
  const chips = () => Array.from(el.querySelectorAll('.tl-sel-chip-name')).map(textOf);
  const status = () => textOf(q('#tl-sel-models-status'));

  function click(target: HTMLElement): void {
    target.click();
    fixture.detectChanges();
  }

  async function search(text: string): Promise<void> {
    const input = q<HTMLInputElement>('#tl-sel-models-search')!;
    input.value = text;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle(250);
    fixture.detectChanges();
  }

  describe('selection band', () => {
    it('is a region labeled by its one status label, which is never empty', () => {
      create(rowsOf(3));
      const band = q('.tl-sel-band')!;
      expect(band.getAttribute('role')).toBe('region');
      expect(band.getAttribute('aria-labelledby')).toBe('tl-sel-models-label');
      expect(band.querySelectorAll('[role="status"]').length).toBe(1);
      expect(label().getAttribute('tabindex')).toBe('-1');
      expect(textOf(label())).toBe('Your selection — All models — no filter');
      expect(textOf(label().querySelector('.visually-hidden'))).toBe('Your selection —');
      expect(q('#tl-sel-models-clear')).toBeNull();
    });

    it('reads 1 of 3 models for a partial choice and All 3 models chosen for every row', () => {
      create(rowsOf(3));
      click(checkbox(0));
      expect(emitted).toEqual([['Anthropic|model-1']]);
      expect(textOf(label())).toBe('Your selection — 1 of 3 models');

      click(q('#tl-sel-models-select-all')!);
      expect(textOf(label())).toBe('Your selection — All 3 models chosen — the same as no filter');
      expect(document.activeElement).toBe(label());
    });

    it('offers Clear selection with the count hidden, and moves focus to the label after it', () => {
      create(rowsOf(3), { selected: ['Anthropic|model-2', 'Anthropic|model-1'] });
      const clear = q<HTMLButtonElement>('#tl-sel-models-clear')!;
      expect(clear.classList).toContain('btn-ghost');
      expect(textOf(clear)).toBe('Clear selection (2)');
      expect(textOf(clear.querySelector('.visually-hidden'))).toBe('(2)');

      click(clear);
      expect(emitted[emitted.length - 1]).toEqual([]);
      expect(q('#tl-sel-models-clear')).toBeNull();
      expect(document.activeElement).toBe(label());
    });

    it('shows one chip per chosen model in choice order, each with a named remove button and a hint', () => {
      create(rowsOf(3), { selected: ['Anthropic|model-3', 'Anthropic|model-1'] });
      expect(chips()).toEqual(['Model 3', 'Model 1']);
      const remove = q<HTMLButtonElement>('.tl-sel-chip-remove')!;
      expect(remove.classList).toContain('action-btn');
      expect(remove.getAttribute('aria-label')).toBe('Remove Model 3 from the selection');
      const tip = q('#' + remove.getAttribute('interestfor'))!;
      expect(tip.getAttribute('popover')).toBe('hint');
      expect(textOf(tip)).toBe('Remove from the selection');
    });

    it('moves focus after a removal to the next chip, else Clear selection, else the label', () => {
      create(rowsOf(4), { selected: ['Anthropic|model-1', 'Anthropic|model-2', 'Anthropic|model-3'] });
      const removes = () => Array.from(el.querySelectorAll<HTMLButtonElement>('.tl-sel-chip-remove'));

      click(removes()[0]);
      expect(emitted[emitted.length - 1]).toEqual(['Anthropic|model-2', 'Anthropic|model-3']);
      expect(document.activeElement).toBe(removes()[0]);
      expect(document.activeElement?.getAttribute('aria-label')).toBe('Remove Model 2 from the selection');

      click(removes()[1]);
      expect(chips()).toEqual(['Model 2']);
      expect(document.activeElement?.id).toBe('tl-sel-models-clear');

      click(removes()[0]);
      expect(chips()).toEqual([]);
      expect(document.activeElement).toBe(label());
    });

    it('offers Show all N while the chips overflow, and Show fewer while they wrap', async () => {
      create(rowsOf(12, i => ({ modelName: `A rather long model name number ${i}` })));
      el.style.inlineSize = '320px';
      fixture.componentRef.setInput('selectedKeys', rowsOf(12).map(r => r.key));
      fixture.detectChanges();
      await settle(50);
      fixture.detectChanges();

      const more = q<HTMLButtonElement>('.tl-sel-more')!;
      expect(textOf(more)).toBe('Show all 12');
      expect(more.getAttribute('aria-controls')).toBe('tl-sel-models-chips');
      expect(more.getAttribute('aria-expanded')).toBe('false');

      click(more);
      expect(textOf(q('.tl-sel-more'))).toBe('Show fewer');
      expect(q('.tl-sel-more')!.getAttribute('aria-expanded')).toBe('true');
      expect(q('#tl-sel-models-chips')!.classList).toContain('is-expanded');
    });

    it('focuses its label on focusLabel()', () => {
      create(rowsOf(2));
      component.focusLabel();
      expect(document.activeElement).toBe(label());
    });
  });

  describe('hint, filter bar and status', () => {
    it('says what a choice means', () => {
      create(rowsOf(2));
      expect(textOf(q('.tl-charts-hint'))).toBe(
        'Choose the models to chart. With none chosen — or all of them — every model counts. The figures cover the date range, keys and roles.');
    });

    it('has a labeled search field with autocorrection off', () => {
      create(rowsOf(2));
      const input = q<HTMLInputElement>('#tl-sel-models-search')!;
      expect(textOf(el.querySelector('label[for="tl-sel-models-search"]'))).toBe('Search models by name, model id or provider');
      expect(input.getAttribute('autocomplete')).toBe('off');
      expect(input.getAttribute('autocorrect')).toBe('off');
      expect(input.getAttribute('autocapitalize')).toBe('off');
      expect(input.getAttribute('spellcheck')).toBe('false');
      expect(input.getAttribute('enterkeyhint')).toBe('done');
    });

    it('searches name, model id and provider once typing pauses, and marks the matches', async () => {
      create(rowsOf(3, i => (i === 2 ? { provider: 'OpenAI', modelId: 'gpt-x', modelName: 'GPT X' } : {})));
      const input = q<HTMLInputElement>('#tl-sel-models-search')!;
      input.value = 'gpt';
      input.dispatchEvent(new Event('input', { bubbles: true }));
      fixture.detectChanges();
      expect(names().length).toBe(3);

      await settle(250);
      fixture.detectChanges();
      expect(names()).toEqual(['GPT X']);
      expect(Array.from(el.querySelectorAll('mark.model-option-match')).map(textOf)).toEqual(['GPT', 'gpt']);
      expect(status()).toBe('Showing 1 of 3 models');

      await search('openai');
      expect(names()).toEqual(['GPT X']);
    });

    it('clears the search on Escape with text and stops the key; with none, Escape passes', async () => {
      create(rowsOf(3));
      await search('model 1');
      const input = q<HTMLInputElement>('#tl-sel-models-search')!;

      const composing = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true, isComposing: true });
      input.dispatchEvent(composing);
      expect(composing.defaultPrevented).toBe(false);

      const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      input.dispatchEvent(escape);
      fixture.detectChanges();
      expect(escape.defaultPrevented).toBe(true);
      expect(input.value).toBe('');
      expect(names().length).toBe(3);

      const again = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
      input.dispatchEvent(again);
      expect(again.defaultPrevented).toBe(false);
    });

    it('sorts by the Sort popover, remembering the sort', () => {
      create(rowsOf(3));
      expect(names()).toEqual(['Model 1', 'Model 2', 'Model 3']);
      expect(textOf(q('#tl-sel-models-sort-trigger'))).toBe('Sort: Requests, highest first');

      click(q<HTMLInputElement>('#tl-sel-models-sort-field-name')!);
      expect(names()).toEqual(['Model 3', 'Model 2', 'Model 1']);
      click(q<HTMLInputElement>('#tl-sel-models-sort-direction-asc')!);
      expect(names()).toEqual(['Model 1', 'Model 2', 'Model 3']);
      expect(JSON.parse(localStorage.getItem(TL_CHARTS_MODELS_VIEW_STORAGE_KEY)!)).toEqual({
        version: 1, sort: { field: 'name', direction: 'asc' }, providers: []
      });
    });

    it('filters by provider toggles from two providers, remembering them; Clear filters drops them', () => {
      create(rowsOf(3, i => (i === 3 ? { provider: 'OpenAI' } : {})));
      const openai = q<HTMLButtonElement>('#tl-sel-models-provider-openai')!;
      expect(openai.getAttribute('aria-pressed')).toBe('false');
      expect(textOf(openai.querySelector('span'))).toBe('OpenAI');
      expect(textOf(openai.querySelector('.model-picker-provider-count'))).toBe('1 model');

      click(openai);
      expect(q('#tl-sel-models-provider-openai')!.getAttribute('aria-pressed')).toBe('true');
      expect(q('#tl-sel-models-provider-openai .model-picker-provider-check')).not.toBeNull();
      expect(names()).toEqual(['Model 3']);
      expect(JSON.parse(localStorage.getItem(TL_CHARTS_MODELS_VIEW_STORAGE_KEY)!).providers).toEqual(['openai']);

      click(q('#tl-sel-models-clear-filters')!);
      expect(names().length).toBe(3);
      expect(document.activeElement?.id).toBe('tl-sel-models-search');
      expect(JSON.parse(localStorage.getItem(TL_CHARTS_MODELS_VIEW_STORAGE_KEY)!).providers).toEqual([]);
    });

    it('hides the provider toggles for one provider', () => {
      create(rowsOf(3));
      expect(q('.tl-sel-providers')).toBeNull();
    });

    it('offers System and Own keys only under All keys with both present', () => {
      const rows = rowsOf(3, i => (i === 2 ? { keySources: ['user'] } : {}));
      create(rows, { keys: 'system' });
      expect(q('.tl-sel-key-sources')).toBeNull();
      fixture.destroy();

      create(rows, { keys: 'both' });
      const own = q<HTMLButtonElement>('#tl-sel-models-key-user')!;
      expect(textOf(own.querySelector('span'))).toBe('Own keys');
      expect(textOf(own.querySelector('.model-picker-provider-count'))).toBe('1 model');
      click(own);
      expect(names()).toEqual(['Model 2']);
      expect(textOf(el.querySelector('.tl-sel-row-meta .tl-sel-key-badge'))).toBe('key Own key');
    });

    it('offers Selected only while something is chosen', () => {
      create(rowsOf(3));
      expect(q('#tl-sel-models-selected-only')).toBeNull();
      click(checkbox(1));
      const toggle = q<HTMLButtonElement>('#tl-sel-models-selected-only')!;
      expect(toggle.classList).toContain('gh-filter-toggle');
      expect(toggle.getAttribute('aria-pressed')).toBe('false');

      click(toggle);
      expect(q('#tl-sel-models-selected-only')!.getAttribute('aria-pressed')).toBe('true');
      expect(names()).toEqual(['Model 2']);
      expect(status()).toBe('Showing 1 of 3 models · 1 chosen');
    });
  });

  describe('Select all and Select all shown', () => {
    it('offers Select all while the list is not narrowed, choosing every row uncapped', () => {
      create(rowsOf(60), { max: 50 });
      click(q('#tl-sel-models-select-all')!);
      expect(emitted[emitted.length - 1].length).toBe(60);
      expect(q('#tl-sel-models-select-all')).toBeNull();
      expect(q('.tl-sel-cap')).toBeNull();
    });

    it('offers Select all shown (N) while the list is narrowed, adding the shown rows', async () => {
      create(rowsOf(12));
      await search('model 1');
      expect(q('#tl-sel-models-select-all')).toBeNull();
      const shown = q<HTMLButtonElement>('#tl-sel-models-select-shown')!;
      expect(textOf(shown)).toBe('Select all shown (4)');

      click(shown);
      expect(emitted[emitted.length - 1]).toEqual(['Anthropic|model-1', 'Anthropic|model-10', 'Anthropic|model-11', 'Anthropic|model-12']);
      const after = q<HTMLButtonElement>('#tl-sel-models-select-shown')!;
      expect(after.getAttribute('aria-disabled')).toBe('true');
      expect(textOf(q('#' + after.getAttribute('interestfor')))).toBe('Every model shown is already chosen.');
    });

    it('adds the shown rows up to the cap, then refuses with the reason', async () => {
      create(rowsOf(12), { max: 2 });
      await search('model 1');
      click(q('#tl-sel-models-select-shown')!);
      expect(emitted[emitted.length - 1]).toEqual(['Anthropic|model-1', 'Anthropic|model-10']);
      const shown = q<HTMLButtonElement>('#tl-sel-models-select-shown')!;
      expect(shown.getAttribute('aria-disabled')).toBe('true');
      expect(textOf(q('#' + shown.getAttribute('interestfor')))).toBe('At most 2 models can be chosen.');
      const count = emitted.length;
      click(shown);
      expect(emitted.length).toBe(count);
    });
  });

  describe('the cap', () => {
    it('refuses an unchecked checkbox at the cap, which keeps its focus and is described by the warning', () => {
      create(rowsOf(4), { max: 2 });
      click(checkbox(0));
      click(checkbox(1));
      const warning = q('#tl-sel-models-cap')!;
      expect(warning.classList).toContain('gh-field-warning');
      expect(textOf(warning)).toBe('At most 2 models can be chosen — or choose them all with Select all.');

      const third = checkbox(2);
      expect(third.getAttribute('aria-disabled')).toBe('true');
      expect(third.getAttribute('aria-describedby')).toBe('tl-sel-models-opt-2-meta tl-sel-models-cap');
      expect(checkbox(0).hasAttribute('aria-disabled')).toBe(false);

      let prevented = false;
      el.addEventListener('click', event => { prevented = event.defaultPrevented; }, { once: true });
      third.focus();
      click(third);
      expect(prevented).toBe(true);
      expect(third.checked).toBe(false);
      expect(emitted.length).toBe(2);
      expect(document.activeElement).toBe(third);
    });

    it('lets the last unchosen row complete a full choice, which is no filter', () => {
      create(rowsOf(3), { max: 2, selected: ['Anthropic|model-1', 'Anthropic|model-2'] });
      expect(q('.tl-sel-cap')).not.toBeNull();
      expect(checkbox(2).hasAttribute('aria-disabled')).toBe(false);
      click(checkbox(2));
      expect(emitted[emitted.length - 1].length).toBe(3);
      expect(q('.tl-sel-cap')).toBeNull();
    });
  });

  describe('the list', () => {
    it('is a list of checkbox labels, each with a name and a meta line of badges and parts', () => {
      create(modelRows({ models: [option(1, { keySources: ['system', 'user'], estimatedCost: 0.25 })] }));
      const list = q('ul.tl-sel-list')!;
      expect(list.getAttribute('role')).toBe('list');
      const row = q('label.checkbox-label.align-start.tl-sel-row')!;
      expect(row.querySelector('input[type="checkbox"]')).not.toBeNull();
      expect(checkbox(0).getAttribute('aria-labelledby')).toBe('tl-sel-models-opt-0-name');
      const meta = q('#tl-sel-models-opt-0-meta')!;
      expect(textOf(meta.querySelector('code'))).toBe('model-1');
      expect(meta.querySelector('app-provider-badge')).not.toBeNull();
      expect(Array.from(meta.querySelectorAll('.config-badge')).map(textOf)).toEqual(['key System', 'key Own key']);
      const parts = Array.from(meta.querySelectorAll('.tl-sel-parts > span:not(.tl-sel-dot):not(.visually-hidden)')).map(textOf);
      expect(parts).toEqual(['999 requests', 'est. $0.2500', 'last used 2026-10-12']);
      expect(meta.querySelectorAll('.tl-sel-dot[aria-hidden="true"]').length).toBe(2);
      expect(Array.from(meta.querySelectorAll('.tl-sel-parts > .visually-hidden')).map(e => e.textContent)).toEqual([', ', ', ']);
    });

    it('keeps a chosen key the rows lack chosen, listed first with No usage in this scope, named as last seen', () => {
      create(rowsOf(2));
      click(checkbox(1));
      fixture.componentRef.setInput('rows', rowsOf(3).filter(r => r.key !== 'Anthropic|model-2'));
      fixture.detectChanges();

      expect(names()).toEqual(['Model 2', 'Model 1', 'Model 3']);
      expect(checkbox(0).checked).toBe(true);
      expect(textOf(q('#tl-sel-models-opt-0-meta'))).toContain('No usage in this scope');
      expect(q('.tl-sel-row-absent')).not.toBeNull();
      expect(chips()).toEqual(['Model 2']);
    });

    it('names a chosen key it never saw by its model id', () => {
      create(rowsOf(1), { selected: ['Mistral|mistral-big'] });
      expect(names()[0]).toBe('mistral-big');
      expect(textOf(label())).toBe('Your selection — 1 of 1 model');
    });

    it('shows 50 rows, then Show 50 more and Show all M, focusing the first new checkbox', () => {
      create(rowsOf(120));
      expect(names().length).toBe(50);
      expect(status()).toBe('Showing 50 of 120 models');
      expect(textOf(q('.tl-sel-show-more'))).toBe('Show 50 more');
      expect(textOf(q('.tl-sel-show-all'))).toBe('Show all 120');

      click(q('.tl-sel-show-more')!);
      expect(names().length).toBe(100);
      expect(document.activeElement).toBe(checkbox(50));
      expect(checkbox(50).closest('li')!.classList).toContain('tl-sel-item-deferred');
      expect(checkbox(49).closest('li')!.classList).not.toContain('tl-sel-item-deferred');
      expect(q('.tl-sel-show-all')).toBeNull();

      click(q('.tl-sel-show-more')!);
      expect(names().length).toBe(120);
      expect(document.activeElement).toBe(checkbox(100));
      expect(q('.tl-sel-show-more')).toBeNull();
    });

    it('returns to one batch for new rows', () => {
      create(rowsOf(120));
      click(q('.tl-sel-show-all')!);
      expect(names().length).toBe(120);
      fixture.componentRef.setInput('rows', rowsOf(80));
      fixture.detectChanges();
      expect(names().length).toBe(50);
    });
  });

  describe('states', () => {
    it('shows the loading indicator while loading', () => {
      create([]);
      fixture.componentRef.setInput('loading', true);
      fixture.detectChanges();
      expect(q('app-loading-indicator')).not.toBeNull();
      expect(q('.tl-sel-filter-bar')).toBeNull();
      expect(status()).toBe('');
    });

    it('shows an error with Retry', () => {
      create([]);
      fixture.componentRef.setInput('error', 'The models could not be loaded: boom');
      fixture.detectChanges();
      expect(textOf(q('.gh-field-error'))).toBe('The models could not be loaded: boom');
      let retried = 0;
      component.retry.subscribe(() => retried++);
      const retry = q<HTMLButtonElement>('#tl-sel-models-retry')!;
      expect(retry.classList).toContain('btn-ghost');
      expect(textOf(retry)).toBe('Retry');
      click(retry);
      expect(retried).toBe(1);
    });

    it('says when no model was used', () => {
      create([]);
      expect(textOf(q('.tl-sel-empty'))).toBe('No model was used in this date range with these keys and roles.');
      expect(q('.tl-sel-list')).toBeNull();
      expect(textOf(label())).toBe('Your selection — All models — no filter');
    });
  });
});
