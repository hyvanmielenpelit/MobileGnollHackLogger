import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BenchmarkCostPanelComponent } from './benchmark-cost-panel.component';

/**
 * The per-role cost panel.
 *
 * Two properties carry most of the weight here. The first is that the role order is **fixed**:
 * the panel repaints every two seconds while a run executes, so a row order derived from the
 * figures would rearrange itself under the operator's eyes. The second is that a null role is
 * absent and a zero role is present — zero is a measurement, and collapsing the two would make
 * "the claim verifier cost nothing" indistinguishable from "no claim verifier ran".
 */
describe('BenchmarkCostPanelComponent', () => {
  let component: BenchmarkCostPanelComponent;
  let fixture: ComponentFixture<BenchmarkCostPanelComponent>;

  /**
   * Applies inputs the way the framework does, through `setInput`.
   *
   * A plain property write does not mark the view dirty, and change detection here refreshes
   * only dirty views — so the initial render picks a written property up, and every write after
   * it renders nothing. A test that mutated an input and re-rendered would then assert against
   * the previous frame and pass or fail on whichever value happened to be on screen.
   */
  function setInputs(inputs: Record<string, unknown>): void {
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
  }

  /** A run with every role priced, so all five lines and the subtotal render. */
  function fillEveryRole(): void {
    setInputs({
      total: 4.0,
      candidate: 1.0,
      assessor: 2.0,
      secondOpinion: 0.5,
      claimVerifier: 0.3,
      synthesis: 0.2,
      grading: 3.0,
      pricingSource: 'Configured model pricing'
    });
  }

  function roleNames(): string[] {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
        '.gh-cost-role:not(.gh-cost-role--subtotal) .gh-cost-role__name'
      )
    ).map(el => (el.textContent ?? '').trim());
  }

  function roleShares(): number[] {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
        '.gh-cost-role:not(.gh-cost-role--subtotal) .gh-cost-role__share'
      )
    ).map(el => Number.parseInt((el.textContent ?? '').trim(), 10));
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BenchmarkCostPanelComponent]
    }).compileComponents();

    fixture = TestBed.createComponent(BenchmarkCostPanelComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  describe('role rows', () => {
    it('should render the roles in the fixed order, subtotal last', () => {
      fillEveryRole();
      fixture.detectChanges();

      expect(roleNames()).toEqual([
        'Model under test',
        'Assessor',
        'Second opinion',
        'Claim verifier',
        'Final synthesis'
      ]);

      const rows = fixture.nativeElement.querySelectorAll('.gh-cost-role');
      expect(rows.length).toBe(6);
      expect(rows[5].classList).toContain('gh-cost-role--subtotal');
    });

    it('should keep the fixed order when the largest figure is the last role', () => {
      fillEveryRole();
      setInputs({ candidate: 0.01, synthesis: 9.0 });
      fixture.detectChanges();

      expect(roleNames()[0]).toBe('Model under test');
      expect(roleNames()[4]).toBe('Final synthesis');
    });

    it('should omit a null role and show a zero role', () => {
      fillEveryRole();
      setInputs({ secondOpinion: null, claimVerifier: 0 });
      fixture.detectChanges();

      expect(roleNames()).toEqual([
        'Model under test',
        'Assessor',
        'Claim verifier',
        'Final synthesis'
      ]);

      const zeroRow = roleNames().indexOf('Claim verifier');
      const amounts = Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
          '.gh-cost-role:not(.gh-cost-role--subtotal) .gh-cost-role__amount'
        )
      ).map(el => (el.textContent ?? '').trim());
      expect(amounts[zeroRow]).toBe('$0.00');
    });

    it('should render no role list at all when every role is null', () => {
      setInputs({ total: 1.5 });
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.gh-cost-panel__roles')).toBeNull();
    });
  });

  describe('shares', () => {
    it('should sum to 100 per cent', () => {
      fillEveryRole();
      fixture.detectChanges();

      const shares = roleShares();
      expect(shares.length).toBe(5);
      expect(shares.reduce((running, share) => running + share, 0)).toBe(100);
    });

    it('should still sum to 100 per cent for figures that do not round cleanly', () => {
      setInputs({
        candidate: 1,
        assessor: 1,
        secondOpinion: 1,
        claimVerifier: 1,
        synthesis: 1,
        grading: 4
      });
      fixture.detectChanges();

      const shares = roleShares();
      expect(shares.reduce((running, share) => running + share, 0)).toBe(100);
      expect(shares).toEqual([20, 20, 20, 20, 20]);
    });

    it('should report a zero share for every role when nothing was spent', () => {
      setInputs({ candidate: 0, assessor: 0 });
      fixture.detectChanges();

      expect(roleShares()).toEqual([0, 0]);
    });
  });

  describe('grading subtotal', () => {
    it('should render the grading input rather than a client-side sum', () => {
      fillEveryRole();
      setInputs({ grading: 2.75 });
      fixture.detectChanges();

      const subtotal = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.gh-cost-role--subtotal');
      expect(subtotal).not.toBeNull();
      expect(subtotal!.querySelector('.gh-cost-role__name')!.textContent!.trim()).toBe('Grading subtotal');
      expect(subtotal!.querySelector('.gh-cost-role__amount')!.textContent!.trim()).toBe('$2.75');
    });

    it('should carry no bar and no share', () => {
      fillEveryRole();
      fixture.detectChanges();

      const subtotal = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.gh-cost-role--subtotal');
      expect(subtotal!.querySelector('.gh-cost-role__bar')).toBeNull();
      expect(subtotal!.querySelector('.gh-cost-role__share')).toBeNull();
    });

    it('should be absent when no grading figure was supplied', () => {
      fillEveryRole();
      setInputs({ grading: null });
      fixture.detectChanges();

      expect(fixture.nativeElement.querySelector('.gh-cost-role--subtotal')).toBeNull();
    });
  });

  describe('legacy note', () => {
    it('should appear only when the run predates per-role cost tracking', () => {
      fillEveryRole();
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).not.toContain('per-role cost tracking');

      setInputs({ legacyRun: true });
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain('per-role cost tracking');
    });
  });

  describe('variant', () => {
    it('should head the live panel differently from the final one', () => {
      fillEveryRole();
      setInputs({ variant: 'live' });
      fixture.detectChanges();
      const liveHeading = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.gh-cost-panel__title')!.textContent!.trim();

      setInputs({ variant: 'final' });
      fixture.detectChanges();
      const finalHeading = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.gh-cost-panel__title')!.textContent!.trim();

      expect(liveHeading).toBe('Estimated cost so far');
      expect(finalHeading).toBe('Estimated cost');
      expect(liveHeading).not.toBe(finalHeading);
    });

    it('should name the panel through its heading, with no live region', () => {
      fillEveryRole();
      fixture.detectChanges();

      const section = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.gh-cost-panel')!;
      const heading = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.gh-cost-panel__title')!;
      expect(section.getAttribute('aria-labelledby')).toBe(heading.id);
      expect(fixture.nativeElement.querySelector('[aria-live]')).toBeNull();
      expect(fixture.nativeElement.querySelector('[role="status"]')).toBeNull();
    });
  });

  describe('pricing provenance', () => {
    it('should name the pricing source, falling back to an explicit unknown', () => {
      fillEveryRole();
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('.gh-cost-panel__prov')!.textContent!.trim())
        .toBe('Configured model pricing');

      setInputs({ pricingSource: null });
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('.gh-cost-panel__prov')!.textContent!.trim())
        .toBe('Pricing unknown');
    });

    it('should mark and explain an incomplete total', () => {
      fillEveryRole();
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('.gh-cost-panel__marker')).toBeNull();

      setInputs({ pricingIncomplete: true });
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('.gh-cost-panel__marker')).not.toBeNull();
      expect(fixture.nativeElement.textContent).toContain('lower bound');
    });
  });

  describe('formatAmount', () => {
    it('should use two decimals at or above a dollar and four below', () => {
      expect(component.formatAmount(2.5312)).toBe('$2.53');
      expect(component.formatAmount(1)).toBe('$1.00');
      expect(component.formatAmount(0.9912)).toBe('$0.9912');
      expect(component.formatAmount(0.0004)).toBe('$0.0004');
    });

    it('should render a missing figure as a dash', () => {
      expect(component.formatAmount(null)).toBe('-');
      expect(component.formatAmount(undefined)).toBe('-');
      expect(component.formatAmount(Number.NaN)).toBe('-');
    });
  });
});
