import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BenchmarkSynthesisPanelComponent, BenchmarkSynthesisView } from './benchmark-synthesis-panel.component';
import { BenchmarkSynthesisConvergenceRowDto } from '../../../services/admin-benchmark.service';

/**
 * The run detail's synthesis panel: one card for a single synthesis, and for a panel run's two a
 * tab row that must honour the whole frontend_ui_controls §5 contract.
 */
describe('BenchmarkSynthesisPanelComponent', () => {
  let fixture: ComponentFixture<BenchmarkSynthesisPanelComponent>;

  /** Inputs through setInput, which marks the OnPush view dirty as the framework does. */
  function setInputs(inputs: Record<string, unknown>): void {
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
  }

  function synthesis(overrides: Partial<BenchmarkSynthesisView> = {}): BenchmarkSynthesisView {
    return {
      key: 'A',
      memberLabel: 'Member A',
      modelLabel: 'GPT-5 Mini',
      provider: 'OpenAI',
      familyRelation: 'same-family',
      text: 'Accurate overall.',
      findings: [
        { kind: 'strength', category: 'accuracy', questions: [1, 2], text: 'Cites the source.' },
        { kind: 'weakness', category: 'critical_error', questions: [], text: 'Invents a spell.' }
      ],
      holisticScore: 81,
      parseFailed: false,
      rawJson: null,
      ...overrides
    };
  }

  const memberB = (): BenchmarkSynthesisView => synthesis({
    key: 'B',
    memberLabel: 'Member B',
    modelLabel: 'Claude Sonnet',
    provider: 'Anthropic',
    familyRelation: 'cross-family',
    text: 'Mostly complete.',
    findings: [],
    holisticScore: 77
  });

  const convergence: BenchmarkSynthesisConvergenceRowDto[] = [
    { kind: 'strength', category: 'accuracy', questions: [1, 4], status: 'Convergent', memberAText: 'Cites the source.', memberBText: 'Grounded in source.' },
    { kind: 'weakness', category: 'critical_error', questions: [], status: 'MemberAOnly', memberAText: 'Invents a spell.', memberBText: null },
    { kind: 'strength', category: 'completeness', questions: [2], status: 'Conflicting', memberAText: 'Covers every case.', memberBText: 'Skips the prerequisite.' }
  ];

  function tabs(): HTMLButtonElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('[role="tab"]'));
  }

  function panel(): HTMLElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('[role="tabpanel"]');
  }

  function press(tab: HTMLElement, key: string): void {
    tab.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BenchmarkSynthesisPanelComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(BenchmarkSynthesisPanelComponent);
  });

  it('should render nothing without a synthesis', () => {
    setInputs({ syntheses: [] });
    expect((fixture.nativeElement as HTMLElement).querySelector('.synthesis-card')).toBeNull();
  });

  describe('one synthesis', () => {
    it('should render no tablist, only the heading and the prose', () => {
      setInputs({ syntheses: [synthesis({ memberLabel: 'Assessor' })] });

      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('[role="tablist"]')).toBeNull();
      expect(el.querySelector('[role="tabpanel"]')).toBeNull();
      expect(el.querySelector('h4')!.textContent!.trim()).toBe('Assessor Qualitative Synthesis');
      expect(el.querySelector('.prose-review')!.textContent).toContain('Accurate overall.');
    });

    it('should list the findings under Strengths and Weaknesses, with their questions', () => {
      setInputs({ syntheses: [synthesis()] });

      const el = fixture.nativeElement as HTMLElement;
      const headings = Array.from(el.querySelectorAll('.findings-heading')).map(h => h.textContent!.trim());
      expect(headings).toEqual(['Strengths', 'Weaknesses']);

      const items = Array.from(el.querySelectorAll('.findings-list li')).map(li => li.textContent!.replace(/\s+/g, ' ').trim());
      expect(items[0]).toContain('accuracy');
      expect(items[0]).toContain('Cites the source.');
      expect(items[0]).toContain('(Q1, Q2)');
      expect(items[1]).toContain('critical error');
      expect(items[1]).not.toContain('(');
    });

    it('should render the synthesis, its findings and the raw output as plain text, never as HTML', () => {
      setInputs({
        syntheses: [synthesis({
          text: '<div id="synthesis-html">Bold <b>claim</b></div>',
          findings: [{ kind: 'weakness', category: 'other', questions: [], text: '<img src=x onerror="alert(1)">' }],
          parseFailed: true,
          rawJson: '{"html":"<script>alert(1)</script>"}'
        })]
      });

      const el = fixture.nativeElement as HTMLElement;
      const prose = el.querySelector('.prose-review')!;
      expect(prose.querySelector('#synthesis-html')).toBeNull();
      expect(prose.textContent).toContain('<div id="synthesis-html">');
      expect(el.querySelector('.findings-list img')).toBeNull();
      expect(el.querySelector('.findings-list')!.textContent).toContain('<img src=x');
      expect(el.querySelector('.synthesis-raw script')).toBeNull();
      expect(el.querySelector('.synthesis-raw pre')!.textContent).toContain('<script>');
    });

    it('should say when the synthesis could not be parsed', () => {
      setInputs({ syntheses: [synthesis({ parseFailed: true, rawJson: '{oops' })] });
      expect((fixture.nativeElement as HTMLElement).querySelector('.synthesis-parse-note')).not.toBeNull();
    });
  });

  describe('two syntheses', () => {
    beforeEach(() => {
      setInputs({ syntheses: [synthesis(), memberB()], convergence });
    });

    it('should render three tabs, one per member and Agreement, with the tab ARIA contract', () => {
      const tablist = (fixture.nativeElement as HTMLElement).querySelector('[role="tablist"]')!;
      expect(tablist.getAttribute('aria-label')).toBe('Syntheses by panel member');
      expect(tablist.getAttribute('aria-label')!.toLowerCase()).not.toContain('tab');

      const all = tabs();
      expect(all.length).toBe(3);
      expect(all[0].textContent).toContain('Member A: GPT-5 Mini');
      expect(all[0].textContent).toContain('same-family');
      expect(all[1].textContent).toContain('Member B: Claude Sonnet');
      expect(all[1].textContent).toContain('cross-family');
      expect(all[2].textContent!.trim()).toBe('Agreement');

      for (const tab of all) {
        expect(tab.getAttribute('type')).toBe('button');
        expect(tab.classList).toContain('gh-tab');
        expect(tab.id).toBeTruthy();
        expect(tab.getAttribute('aria-controls')).toBeTruthy();
      }
      expect(all.map(t => t.getAttribute('aria-selected'))).toEqual(['true', 'false', 'false']);
      expect(all.map(t => t.getAttribute('tabindex'))).toEqual(['0', '-1', '-1']);

      const shown = panel()!;
      expect(shown.id).toBe(all[0].getAttribute('aria-controls')!);
      expect(shown.getAttribute('aria-labelledby')).toBe(all[0].id);
      expect(shown.getAttribute('tabindex')).toBe('0');
      expect((fixture.nativeElement as HTMLElement).querySelectorAll('[role="tabpanel"]').length).toBe(1);
    });

    it('should switch panels on click', () => {
      tabs()[1].click();
      fixture.detectChanges();

      expect(tabs()[1].getAttribute('aria-selected')).toBe('true');
      expect(panel()!.getAttribute('aria-labelledby')).toBe(tabs()[1].id);
      expect(panel()!.querySelector('.prose-review')!.textContent).toContain('Mostly complete.');
    });

    it('should move selection and focus with the arrow keys, wrapping at both ends', () => {
      press(tabs()[0], 'ArrowRight');
      expect(tabs()[1].getAttribute('aria-selected')).toBe('true');
      expect(document.activeElement).toBe(tabs()[1]);

      press(tabs()[1], 'ArrowRight');
      press(tabs()[2], 'ArrowRight');
      expect(tabs()[0].getAttribute('aria-selected')).toBe('true');
      expect(document.activeElement).toBe(tabs()[0]);

      press(tabs()[0], 'ArrowLeft');
      expect(tabs()[2].getAttribute('aria-selected')).toBe('true');
      expect(document.activeElement).toBe(tabs()[2]);
      expect(tabs().map(t => t.getAttribute('tabindex'))).toEqual(['-1', '-1', '0']);
    });

    it('should jump to the ends with Home and End', () => {
      press(tabs()[0], 'End');
      expect(tabs()[2].getAttribute('aria-selected')).toBe('true');
      expect(document.activeElement).toBe(tabs()[2]);

      press(tabs()[2], 'Home');
      expect(tabs()[0].getAttribute('aria-selected')).toBe('true');
      expect(document.activeElement).toBe(tabs()[0]);
    });

    it('should let other keys through untouched', () => {
      const event = new KeyboardEvent('keydown', { key: 'a', bubbles: true, cancelable: true });
      tabs()[0].dispatchEvent(event);
      fixture.detectChanges();

      expect(event.defaultPrevented).toBeFalse();
      expect(tabs()[0].getAttribute('aria-selected')).toBe('true');
    });

    it('should render the agreement table in the Agreement tab, never on the table element itself', () => {
      tabs()[2].click();
      fixture.detectChanges();

      const shown = panel()!;
      expect(shown.tagName.toLowerCase()).not.toBe('table');
      expect(shown.querySelector('.convergence-summary')!.textContent)
        .toContain('1 convergent, 1 raised by member A only, 0 by member B only, 1 where the members disagree.');

      const rows = Array.from(shown.querySelectorAll('.convergence-table tbody tr'));
      expect(rows.length).toBe(3);
      const first = Array.from(rows[0].querySelectorAll('td')).map(td => td.textContent!.trim());
      expect(first).toEqual(['Strength', 'accuracy', 'Q1, Q4', 'Both members', 'Cites the source.', 'Grounded in source.']);
      const second = Array.from(rows[1].querySelectorAll('td')).map(td => td.textContent!.trim());
      expect(second).toEqual(['Weakness', 'critical error', 'Run-wide', 'Member A only', 'Invents a spell.', '—']);
    });

    it('should mark a conflicting row with both kinds and its own status class', () => {
      tabs()[2].click();
      fixture.detectChanges();

      const row = panel()!.querySelectorAll('.convergence-table tbody tr')[2];
      const cells = Array.from(row.querySelectorAll('td')).map(td => td.textContent!.trim());
      expect(cells).toEqual([
        'Strength (A) / Weakness (B)', 'completeness', 'Q2', 'Members disagree', 'Covers every case.', 'Skips the prerequisite.'
      ]);
      const status = row.querySelector('.convergence-status')!;
      expect(status.classList).toContain('convergence-conflicting');
    });

    it('should leave the disagreement count out of the summary when no row conflicts', () => {
      setInputs({ convergence: convergence.filter(r => r.status !== 'Conflicting') });
      tabs()[2].click();
      fixture.detectChanges();

      expect(panel()!.querySelector('.convergence-summary')!.textContent!.trim())
        .toBe('1 convergent, 1 raised by member A only, 0 by member B only.');
    });

    it('should say so when no agreement was computed', () => {
      setInputs({ convergence: null });
      tabs()[2].click();
      fixture.detectChanges();

      expect(panel()!.querySelector('.convergence-table')).toBeNull();
      expect(panel()!.textContent).toContain('No agreement was computed');
    });

    it('should keep the chosen tab when the inputs refresh', () => {
      tabs()[1].click();
      fixture.detectChanges();

      setInputs({ syntheses: [synthesis(), memberB()], convergence: [...convergence] });
      expect(tabs()[1].getAttribute('aria-selected')).toBe('true');
    });
  });
});
