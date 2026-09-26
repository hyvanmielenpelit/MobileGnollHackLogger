import { ChangeDetectionStrategy, Component, ElementRef, Input, inject } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import {
  BenchmarkSynthesisConvergenceRowDto,
  BenchmarkSynthesisFindingDto
} from '../../../services/admin-benchmark.service';

/** Distinguishes the element ids of the several panels a page may hold. */
let synthesisPanelSequence = 0;

/** How a grader's provider relates to the candidate's: the same provider, or another one. */
export type BenchmarkFamilyRelation = 'same-family' | 'cross-family';

/** One member's closing synthesis, as the panel renders it. */
export interface BenchmarkSynthesisView {
  /** Unique within the panel and safe in an element id: `A` or `B`. */
  key: string;
  /** `Assessor` in a single-assessor run; `Member A` / `Member B` in a panel run. */
  memberLabel: string;
  modelLabel: string;
  provider: string | null;
  /** Null when the candidate's provider is unknown. */
  familyRelation: BenchmarkFamilyRelation | null;
  text: string | null;
  findings: readonly BenchmarkSynthesisFindingDto[];
  holisticScore: number | null;
  parseFailed: boolean;
  /** The stored synthesis JSON, shown only when it could not be parsed. */
  rawJson: string | null;
}

/**
 * The run's closing synthesis. One synthesis renders as a single card; a panel run's two render as
 * a tab row, one tab per member plus an Agreement tab with the computed convergence of their
 * findings.
 *
 * Every text is plain interpolation: a synthesis is model output and is never rendered as HTML.
 */
@Component({
  selector: 'app-benchmark-synthesis-panel',
  standalone: true,
  imports: [NgTemplateOutlet],
  templateUrl: './benchmark-synthesis-panel.component.html',
  styleUrls: ['./benchmark-synthesis-panel.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BenchmarkSynthesisPanelComponent {
  /** The key of the Agreement tab; never a member key. */
  static readonly AGREEMENT_KEY = 'agreement';

  @Input() syntheses: readonly BenchmarkSynthesisView[] = [];

  /** The server's convergence rows. Null when it computed none. */
  @Input() convergence: readonly BenchmarkSynthesisConvergenceRowDto[] | null = null;

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly idPrefix = `bsp-${++synthesisPanelSequence}`;

  /** The tab the operator chose. Kept across input refreshes while that tab still exists. */
  private selectedKey: string | null = null;

  get hasTabs(): boolean {
    return this.syntheses.length > 1;
  }

  /** Tab order: one tab per member, then Agreement. Empty with fewer than two syntheses. */
  get tabKeys(): string[] {
    if (!this.hasTabs) {
      return [];
    }
    return [...this.syntheses.map(s => s.key), BenchmarkSynthesisPanelComponent.AGREEMENT_KEY];
  }

  get activeKey(): string {
    const keys = this.tabKeys;
    return this.selectedKey != null && keys.includes(this.selectedKey) ? this.selectedKey : (keys[0] ?? '');
  }

  isAgreementKey(key: string): boolean {
    return key === BenchmarkSynthesisPanelComponent.AGREEMENT_KEY;
  }

  synthesisFor(key: string): BenchmarkSynthesisView | null {
    return this.syntheses.find(s => s.key === key) ?? null;
  }

  tabId(key: string): string {
    return `${this.idPrefix}-tab-${key}`;
  }

  panelId(key: string): string {
    return `${this.idPrefix}-panel-${key}`;
  }

  selectTab(key: string): void {
    this.selectedKey = key;
  }

  /** Left/Right move and wrap, Home/End jump to the ends, and focus follows selection. */
  onTabKeydown(event: KeyboardEvent, index: number): void {
    const keys = this.tabKeys;
    const targets: Record<string, number> = {
      ArrowRight: index + 1,
      ArrowLeft: index - 1,
      Home: 0,
      End: keys.length - 1
    };
    const requested = Object.prototype.hasOwnProperty.call(targets, event.key) ? targets[event.key] : undefined;
    if (requested === undefined || keys.length === 0) {
      return;
    }

    event.preventDefault();
    const next = keys[(requested + keys.length) % keys.length];
    this.selectTab(next);
    this.host.nativeElement.querySelector<HTMLElement>(`#${this.tabId(next)}`)?.focus();
  }

  strengthsOf(synthesis: BenchmarkSynthesisView): BenchmarkSynthesisFindingDto[] {
    return synthesis.findings.filter(f => (f.kind ?? '').toLowerCase() === 'strength');
  }

  weaknessesOf(synthesis: BenchmarkSynthesisView): BenchmarkSynthesisFindingDto[] {
    return synthesis.findings.filter(f => (f.kind ?? '').toLowerCase() === 'weakness');
  }

  /** `critical_error` reads as `critical error`. */
  categoryLabel(category: string | null | undefined): string {
    return (category ?? '').replace(/_/g, ' ').trim() || 'other';
  }

  kindLabel(kind: string | null | undefined): string {
    const k = (kind ?? '').toLowerCase();
    return k === 'strength' ? 'Strength' : k === 'weakness' ? 'Weakness' : (kind ?? '');
  }

  questionsLabel(questions: readonly number[] | null | undefined): string {
    return (questions ?? []).map(q => `Q${q}`).join(', ');
  }

  statusLabel(status: string | null | undefined): string {
    switch (status) {
      case 'Convergent': return 'Both members';
      case 'MemberAOnly': return 'Member A only';
      case 'MemberBOnly': return 'Member B only';
      default: return status ?? '';
    }
  }

  statusClass(status: string | null | undefined): string {
    switch (status) {
      case 'Convergent': return 'convergence-both';
      case 'MemberAOnly': return 'convergence-a';
      case 'MemberBOnly': return 'convergence-b';
      default: return '';
    }
  }

  get convergenceSummary(): string {
    const rows = this.convergence ?? [];
    if (rows.length === 0) {
      return '';
    }
    const count = (status: string) => rows.filter(r => r.status === status).length;
    return `${count('Convergent')} convergent, ${count('MemberAOnly')} raised by member A only, `
      + `${count('MemberBOnly')} by member B only.`;
  }
}
