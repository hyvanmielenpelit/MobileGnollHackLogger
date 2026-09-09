import { Component, Input } from '@angular/core';
import { DecimalPipe } from '@angular/common';

/** Distinguishes the heading ids of the several panels a page may hold. */
let costPanelSequence = 0;

/** One rendered role line: its name, its formatted amount, and its share of the five roles. */
export interface BenchmarkCostRoleRow {
  key: string;
  name: string;
  amount: number;
  amountLabel: string;
  sharePercent: number;
  shareLabel: string;
}

/**
 * The per-role split behind a run's estimated cost, for both the live progress dialog and the
 * finished-run dialog.
 *
 * Purely presentational: every figure arrives as an input, computed server-side by the same
 * function for a running run and a finished one, so the live panel and the final panel cannot
 * disagree.
 *
 * The role order is fixed — the thing being measured, then the grading roles, then the synthesis
 * that runs last — and is never sorted by cost: a row order that moved as the numbers moved would
 * be unreadable on a panel the live dialog repaints every two seconds.
 */
@Component({
  selector: 'app-benchmark-cost-panel',
  standalone: true,
  templateUrl: './benchmark-cost-panel.component.html',
  styleUrls: ['./benchmark-cost-panel.component.scss']
})
export class BenchmarkCostPanelComponent {
  /** The whole run's estimate, candidate and grading together. */
  @Input() total: number | null = null;

  /** The model under test. */
  @Input() candidate: number | null = null;

  /** The per-question assessments only; the synthesis is a peer role below. */
  @Input() assessor: number | null = null;

  @Input() secondOpinion: number | null = null;
  @Input() claimVerifier: number | null = null;
  @Input() synthesis: number | null = null;

  /** Assessor, second opinion, claim verifier and synthesis together, summed server-side. */
  @Input() grading: number | null = null;

  @Input() pricingSource: string | null = null;

  /** At least one participating model has no price, so the total is a lower bound. */
  @Input() pricingIncomplete = false;

  /** The run predates per-role cost tracking, and its role lines cannot be trusted as a split. */
  @Input() legacyRun = false;

  /** `live` heads the panel *Estimated cost so far*; `final` heads it *Estimated cost*. */
  @Input() variant: 'live' | 'final' = 'final';

  readonly headingId = `gh-cost-panel-title-${++costPanelSequence}`;

  /** The role lines, in render order. A role absent from this list has no figure at all. */
  private static readonly ROLE_ORDER: readonly { key: string; name: string }[] = [
    { key: 'candidate', name: 'Model under test' },
    { key: 'assessor', name: 'Assessor' },
    { key: 'secondOpinion', name: 'Second opinion' },
    { key: 'claimVerifier', name: 'Claim verifier' },
    { key: 'synthesis', name: 'Final synthesis' }
  ];

  get heading(): string {
    return this.variant === 'live' ? 'Estimated cost so far' : 'Estimated cost';
  }

  get totalLabel(): string {
    return this.formatAmount(this.total);
  }

  get gradingLabel(): string {
    return this.formatAmount(this.grading);
  }

  get hasGradingSubtotal(): boolean {
    return this.isFigure(this.grading);
  }

  /**
   * The role lines that have a figure, in `ROLE_ORDER`. A null role is omitted and a zero role is
   * kept: zero is a measurement, absence is not.
   */
  get roles(): BenchmarkCostRoleRow[] {
    const figures: Record<string, number | null> = {
      candidate: this.candidate,
      assessor: this.assessor,
      secondOpinion: this.secondOpinion,
      claimVerifier: this.claimVerifier,
      synthesis: this.synthesis
    };

    const present = BenchmarkCostPanelComponent.ROLE_ORDER
      .filter(role => this.isFigure(figures[role.key]))
      .map(role => ({ key: role.key, name: role.name, amount: figures[role.key] as number }));

    const shares = BenchmarkCostPanelComponent.apportionShares(present.map(role => role.amount));

    return present.map((role, index) => ({
      key: role.key,
      name: role.name,
      amount: role.amount,
      amountLabel: this.formatAmount(role.amount),
      sharePercent: shares[index],
      shareLabel: `${shares[index]}%`
    }));
  }

  /**
   * A dollar amount at the precision the figure deserves: two decimals at or above a dollar, four
   * below, so a cancelled run costing $0.0007 does not collapse to `$0.00`. The same rule as
   * `AdminBenchmarkComponent.formatCostAmount` — restated rather than shared, because importing
   * from the host component that imports this one would be a cycle.
   */
  formatAmount(amount: number | null | undefined): string {
    if (!this.isFigure(amount)) {
      return '-';
    }
    const numPipe = new DecimalPipe('en-US');
    const digits = Math.abs(amount) >= 1 ? '1.2-2' : '1.2-4';
    return `$${numPipe.transform(amount, digits)}`;
  }

  private isFigure(amount: number | null | undefined): amount is number {
    return amount != null && Number.isFinite(amount);
  }

  /**
   * Whole-percent shares that sum to exactly 100 whenever the amounts sum above zero, by giving
   * the leftover points to the largest fractional remainders. Independent rounding of each share
   * would print a column that adds up to 99 or 101.
   */
  private static apportionShares(amounts: readonly number[]): number[] {
    const sum = amounts.reduce((running, amount) => running + amount, 0);
    if (sum <= 0) {
      return amounts.map(() => 0);
    }

    const exact = amounts.map(amount => (amount / sum) * 100);
    const shares = exact.map(share => Math.floor(share));
    let leftover = 100 - shares.reduce((running, share) => running + share, 0);

    const byRemainder = exact
      .map((share, index) => ({ index, remainder: share - Math.floor(share) }))
      .sort((a, b) => b.remainder - a.remainder || a.index - b.index);

    for (const entry of byRemainder) {
      if (leftover <= 0) {
        break;
      }
      shares[entry.index]++;
      leftover--;
    }

    return shares;
  }
}
