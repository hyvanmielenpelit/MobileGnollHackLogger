import { ComponentFixture, TestBed } from '@angular/core/testing';

import { RunFactsComponent } from './run-facts.component';
import { RunFactRow } from './run-facts';

const ROWS: RunFactRow[] = [
  {
    key: 'model', label: 'Model', item: {
      kind: 'models', models: [{
        name: 'GPT-6.1 Sol', provider: 'OpenAI', thinkingLevel: 'high', reasoningMode: 'pro',
        serviceTier: 'flex', customEndpoint: true
      }]
    }
  },
  {
    key: 'assessor', label: 'Assessors', item: {
      kind: 'models', models: [
        { role: 'A', name: 'Claude 5 Opus', provider: 'Anthropic', thinkingLevel: 'high', reasoningMode: null, serviceTier: null, customEndpoint: false },
        { role: 'B', name: 'GPT-5.6 Sol', provider: 'OpenAI', thinkingLevel: 'medium', reasoningMode: null, serviceTier: null, customEndpoint: false }
      ]
    }
  },
  {
    key: 'prompt', label: 'Prompt', item: {
      kind: 'prompt', name: 'Gameplay Help', tags: ['concise', 'tools on', 'snapshot'],
      summary: 'Gameplay Help · concise (tools on) · snapshot'
    }
  },
  { key: 'profile', label: 'Scoring profile', item: { kind: 'text', text: 'Default' } },
  { key: 'started', label: 'Started', item: { kind: 'time', iso: '2026-09-30T13:35:24Z', text: '2026-09-30 13:35:24 UTC' } },
  {
    key: 'board', label: 'Board', item: {
      kind: 'board',
      figures: [{ role: 'Assessor', delivered: 18, total: 18 }, { role: 'Claim verifier', delivered: 16, total: 16 }],
      note: 'Synthesis: yes · Difficulty assessment: digest (no map)',
      gaps: ['co-assessor: Q4']
    }
  }
];

describe('RunFactsComponent', () => {
  let fixture: ComponentFixture<RunFactsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [RunFactsComponent] }).compileComponents();
    fixture = TestBed.createComponent(RunFactsComponent);
    fixture.componentRef.setInput('rows', ROWS);
    fixture.detectChanges();
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function fact(key: string): HTMLElement {
    return root().querySelector(`[data-fact="${key}"]`) as HTMLElement;
  }

  function text(element: Element | null): string {
    return (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  it('renders a definition list with one term and one value per row, grouped in a div', () => {
    const list = root().querySelector('dl.rr-facts') as HTMLElement;
    expect(list).not.toBeNull();
    const groups = Array.from(list.children) as HTMLElement[];
    expect(groups.map(group => group.tagName)).toEqual(Array(6).fill('DIV'));
    expect(groups.map(group => group.getAttribute('data-fact'))).toEqual(['model', 'assessor', 'prompt', 'profile', 'started', 'board']);
    for (const group of groups) {
      expect(Array.from(group.children).map(child => child.tagName)).toEqual(['DT', 'DD']);
    }
    expect(groups.map(group => text(group.querySelector('dt')))).toEqual(
      ['Model', 'Assessors', 'Prompt', 'Scoring profile', 'Started', 'Board']);
  });

  it('badges the model with visually hidden prefixes on every badge but the provider', () => {
    const model = fact('model');
    expect(text(model.querySelector('.rr-fact-model-name'))).toBe('GPT-6.1 Sol');
    expect(text(model.querySelector('.thinking-badge'))).toBe('thinking level High');
    expect(text(model.querySelector('.thinking-badge .visually-hidden'))).toBe('thinking level');
    expect(text(model.querySelector('.reasoning-badge'))).toBe('reasoning mode pro');
    expect(text(model.querySelector('app-provider-badge'))).toBe('OpenAI');
    expect(model.querySelector('app-provider-badge .visually-hidden')).toBeNull();
    const config = Array.from(model.querySelectorAll('.config-badge')).map(text);
    expect(config).toEqual(['service tier Flex', 'Custom endpoint']);
  });

  it('tags the panel members A and B, each with its own badges', () => {
    const models = Array.from(fact('assessor').querySelectorAll('.rr-fact-model'));
    expect(models.length).toBe(2);
    expect(models.map(m => text(m.querySelector('.model-option-tag')))).toEqual(['Member A', 'Member B']);
    expect(models.map(m => text(m.querySelector('.rr-fact-model-name')))).toEqual(['Claude 5 Opus', 'GPT-5.6 Sol']);
    expect(models.map(m => text(m.querySelector('.thinking-badge')))).toEqual(['thinking level High', 'thinking level Medium']);
    expect(models.map(m => text(m.querySelector('app-provider-badge')))).toEqual(['Anthropic', 'OpenAI']);
  });

  it('lists the prompt options as neutral badges', () => {
    const prompt = fact('prompt');
    expect(text(prompt.querySelector('.rr-fact-prompt-name'))).toBe('Gameplay Help');
    expect(Array.from(prompt.querySelectorAll('.config-badge')).map(text))
      .toEqual(['prompt option concise', 'prompt option tools on', 'prompt option snapshot']);
  });

  it('marks up the start time with its datetime', () => {
    const time = fact('started').querySelector('time') as HTMLTimeElement;
    expect(time.getAttribute('datetime')).toBe('2026-09-30T13:35:24Z');
    expect(text(time)).toBe('2026-09-30 13:35:24 UTC');
  });

  it('reads the board figures as "18 of 18", explains the board in an info tip and shows the gap warning', () => {
    const board = fact('board');
    const items = Array.from(board.querySelectorAll('.rr-fact-inline-list li'));
    expect(items.map(text)).toEqual(['Assessor 18 of /18', 'Claim verifier 16 of /16']);
    // The separator trails every item but the last, so a wrapped line never starts with one.
    expect(items.map(li => getComputedStyle(li, '::after').content)).toEqual(['"·"', 'none']);
    expect(items.map(li => getComputedStyle(li, '::before').content)).toEqual(['none', 'none']);
    expect(items.map(li => getComputedStyle(li).whiteSpace)).toEqual(['nowrap', 'nowrap']);

    const tip = board.querySelector('dd app-info-tip') as HTMLElement;
    expect(tip).not.toBeNull();
    expect(tip.querySelector('button')?.getAttribute('aria-label')).toBe('About Board delivery');
    expect(text(board.querySelector('#rr-board-note-tip')))
      .toBe('The final synthesis sees the board; the difficulty assessment sees a digest of it without the map.');

    const spoken = (li: Element): string => {
      const copy = li.cloneNode(true) as HTMLElement;
      copy.querySelectorAll('[aria-hidden="true"]').forEach(hidden => hidden.remove());
      return text(copy);
    };
    expect(spoken(items[0])).toBe('Assessor 18 of 18');
    expect(items[0].querySelector('[aria-hidden="true"]')?.textContent).toBe('/');
    expect(text(board.querySelector('.rr-fact-warning'))).toBe('Graded without the board — co-assessor: Q4');
  });

  it('carries no title attribute anywhere', () => {
    expect(root().querySelectorAll('[title]').length).toBe(0);
  });

  it('fills a flex column whose items align to baseline, as the run report header does', () => {
    const header = document.createElement('div');
    header.style.cssText = 'display: flex; flex-direction: column; align-items: baseline; width: 1200px;';
    document.body.appendChild(header);
    try {
      header.appendChild(root());
      const width = root().getBoundingClientRect().width;
      expect(width).toBe(1200);
      const name = fact('model').querySelector('.rr-fact-model-name') as HTMLElement;
      expect(name.getBoundingClientRect().height).toBeLessThan(40);
    } finally {
      header.remove();
    }
  });

  it('flows the pairs in a wrapping flex row, not a grid', () => {
    const style = getComputedStyle(root().querySelector('.rr-facts') as HTMLElement);
    expect(style.display).toBe('flex');
    expect(style.flexWrap).toBe('wrap');
    expect(getComputedStyle(fact('model')).display).toBe('flex');
  });

  it('keeps a three-line Board pair from stretching the pair before it', () => {
    const shell = document.createElement('div');
    shell.style.cssText = 'width: 1800px;';
    document.body.appendChild(shell);
    try {
      shell.appendChild(root());
      const short = ROWS.filter(row => row.key === 'profile' || row.key === 'started');
      fixture.componentRef.setInput('rows', short);
      fixture.detectChanges();
      const alone = fact('started').getBoundingClientRect().height;

      const board = ROWS.find(row => row.key === 'board')!;
      const tall: RunFactRow = board.item.kind === 'board'
        ? { ...board, item: { ...board.item, gaps: ['co-assessor: Q4', 'claim verifier: Q7'] } }
        : board;
      fixture.componentRef.setInput('rows', [...short, tall]);
      fixture.detectChanges();

      const started = fact('started').getBoundingClientRect();
      const boardRect = fact('board').getBoundingClientRect();
      // Both on one line, and the Board pair is three lines tall: its list and two warnings.
      expect(boardRect.top).toBeLessThan(started.bottom);
      expect(boardRect.height).toBeGreaterThan(alone * 2.5);
      expect(started.height).toBe(alone);
    } finally {
      shell.remove();
    }
  });

  it('stacks each label above its value with layout="stacked", the facts still side by side', () => {
    const shell = document.createElement('div');
    shell.style.cssText = 'width: 1800px;';
    document.body.appendChild(shell);
    try {
      shell.appendChild(root());
      const keys = ['prompt', 'profile', 'started'];
      fixture.componentRef.setInput('layout', 'stacked');
      fixture.componentRef.setInput('rows', ROWS.filter(row => keys.includes(row.key)));
      fixture.detectChanges();

      expect(root().classList).toContain('rr-facts-stacked');
      const list = getComputedStyle(root().querySelector('.rr-facts') as HTMLElement);
      expect(list.display).toBe('flex');
      expect(list.flexWrap).toBe('wrap');
      for (const key of keys) {
        const pair = fact(key);
        expect(getComputedStyle(pair).flexDirection).withContext(key).toBe('column');
        const term = (pair.querySelector('dt') as HTMLElement).getBoundingClientRect();
        const value = (pair.querySelector('dd') as HTMLElement).getBoundingClientRect();
        expect(term.bottom).withContext(key).toBeLessThanOrEqual(value.top);
      }
      const tops = keys.map(key => fact(key).getBoundingClientRect().top);
      for (const top of tops) {
        expect(Math.abs(top - tops[0])).toBeLessThanOrEqual(1);
      }
    } finally {
      shell.remove();
    }
  });

  it('keeps the inline layout by default', () => {
    const shell = document.createElement('div');
    shell.style.cssText = 'width: 1800px;';
    document.body.appendChild(shell);
    try {
      shell.appendChild(root());
      expect(root().classList).not.toContain('rr-facts-stacked');
      expect(getComputedStyle(fact('profile')).flexDirection).toBe('row');
    } finally {
      shell.remove();
    }
  });

  it('renders nothing but an empty list for no rows', () => {
    fixture.componentRef.setInput('rows', []);
    fixture.detectChanges();
    expect(root().querySelectorAll('dl.rr-facts > div').length).toBe(0);
  });
});
